using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using SalesInventory.Infrastructure.Persistence;
using SalesInventory.Api.Hardening;
using SalesInventory.Api.Logging;
using SalesInventory.Api.RateLimiting;
using SalesInventory.Api.Security;
using Serilog;
using SalesInventory.Infrastructure.Security;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SalesInventory.Infrastructure.Identity;
using SalesInventory.Application;
using SalesInventory.Api.Middleware;
using SalesInventory.Api.Swagger;
using SalesInventory.Application.Interfaces;
using SalesInventory.Infrastructure;
using SalesInventory.Infrastructure.Storage;

// QuestPDF is used under its free Community license
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// A minimal logger for the very start: a failure before the configured logger exists (bad configuration, missing key) is still written
Log.Logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Console().CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Structured logging with Serilog. Levels, sinks (console + daily rolling file) and the output format come from the "Serilog"
    // section of appsettings.json. The formatters used there mask secrets (see LogScrubber), so a password, token, API key or
    // connection string never reaches the console or the files, whatever the code logs.
    LogScrubber.Add(new SecretMasker(builder.Configuration));
    builder.Host.UseSerilog((context, services, logger) => logger
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services) // sinks, enrichers and filters registered in DI (the tests add a capturing sink this way)
        .Enrich.FromLogContext(),
        // Keep the static Log.Logger (the start-up logger) as it is: the host uses its own logger. With several hosts in one process
        // (the integration tests) the static one would be overwritten by the last host and lines would land in the wrong place.
        preserveStaticLogger: true);

    // Production hardening (HSTS, https redirection, forwarded headers, safety-net error handler, secrets only from the environment)
    // and CORS for the Blazor client. Nothing here changes Development, except CORS when origins are configured. See ApiHardeningExtensions.
    builder.AddApiHardening();

    // Security log of every 401/403 (see AuthFailureLoggingMiddleware); the thresholds are in "Security:AuthFailures"
    builder.Services.Configure<AuthFailureOptions>(builder.Configuration.GetSection("Security:AuthFailures"));
    builder.Services.TryAddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<AuthFailureTracker>();

    // Add services to the container.

    builder.Services.Configure<SalesInventory.Application.InventorySettings>(builder.Configuration.GetSection("Inventory"));
    builder.Services.Configure<SalesInventory.Application.ShopSettings>(builder.Configuration.GetSection("Shop"));
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    // Product images are saved under wwwroot/uploads/products and served as static files
    var webRootPath = builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
    builder.Services.AddSingleton<IFileStorage>(new LocalFileStorage(webRootPath));

    // JWT bearer validation reads the same "Jwt" section that Infrastructure binds for token generation
    var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
    if (Encoding.UTF8.GetByteCount(jwtSettings.Key) < 32)
    {
        throw new InvalidOperationException(
            "Jwt:Key is missing or shorter than 32 bytes. In production set the environment variable Jwt__Key; in development use: dotnet user-secrets set \"Jwt:Key\" \"<random 32+ char secret>\"");
    }

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                // Default is 5 minutes of tolerance; zero makes "exp" exact
                ClockSkew = TimeSpan.Zero,
                ValidIssuer = jwtSettings.Issuer,
                ValidAudience = jwtSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key))
            };
        });

    // Named policies (used via [Authorize(Policy = "...")]). Roles vs Policy:
    //  - [Authorize(Roles = "A,B")]: role names are hard-coded on each controller/action; simple, but the rule
    //    is duplicated everywhere and can only express "has any of these roles".
    //  - [Authorize(Policy = "X")]: the rule is defined once here and referenced by name, so changing who may
    //    access a feature is a one-line edit; policies can also combine claims, custom requirements or handlers
    //    (e.g. "role Kho AND claim warehouse=HN"), which Roles cannot.
    // Under the hood RequireRole is the same check as Roles=..., so both styles yield 401 (no/invalid token) and 403 (wrong role).
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy(AuthPolicies.AdminOnly, p => p.RequireRole(AppRoles.Admin));
        options.AddPolicy(AuthPolicies.CanManageInventory, p => p.RequireRole(AppRoles.Admin, AppRoles.Kho));
        options.AddPolicy(AuthPolicies.SalesAccess, p => p.RequireRole(AppRoles.Admin, AppRoles.BanHang));
    });

    // Each call to the assistant costs money, so one signed-in user may make only a limited number of calls per window
    // (AiSafety:RateLimit in appsettings.json: limit, window, algorithm). The counter is kept per user id.
    builder.Services.AddAssistantRateLimiting();

    builder.Services.AddControllers();
    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        // Surface XML <summary> comments from controller actions in Swagger UI
        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        options.IncludeXmlComments(xmlPath);

        // Let Swagger UI attach a Bearer token to requests for testing JWT-protected endpoints
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter the JWT token returned by /api/auth/login."
        });
        // Per-operation requirement: only [Authorize]d actions show the padlock and send the token
        options.OperationFilter<AuthorizeOperationFilter>();
    });

    var app = builder.Build();

    // Tell the developer early whether the assistant can work. Only "configured or not" is logged, never the key itself.
    // The key comes from user-secrets (Development) or the ANTHROPIC_API_KEY / Anthropic__ApiKey environment variable.
    var anthropicKeyConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["Anthropic:ApiKey"])
        || !string.IsNullOrWhiteSpace(builder.Configuration["ANTHROPIC_API_KEY"]);
    if (!anthropicKeyConfigured)
    {
        app.Logger.LogWarning(
            "Anthropic API key is not configured; POST /api/assistant/ask will answer 503. " +
            "Set it with: dotnet user-secrets set \"Anthropic:ApiKey\" \"<your key>\" --project src/SalesInventory.Api");
    }

    // Configure the HTTP request pipeline.
    // First in the pipeline so it catches exceptions thrown by everything after it
    // Production only: believe the reverse proxy about the scheme and client IP, and catch whatever escapes the error handling below
    app.UseApiHardeningFirst();

    // One structured line per request (method, path, status, duration). Outermost, so it sees the final status code, including the
    // ProblemDetails responses the exception middleware below produces.
        app.UseSerilogRequestLogging(options =>
    {
        options.Logger = app.Services.GetRequiredService<Serilog.ILogger>(); // this host's logger, not the static one
        options.MessageTemplate = RequestLogging.MessageTemplate;
        options.GetLevel = RequestLogging.GetLevel;
        options.EnrichDiagnosticContext = RequestLogging.Enrich;
    });

    app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseApiHsts(); // production only; must come before the redirection
    app.UseHttpsRedirection();

    // Serves wwwroot (including uploaded product images under /uploads/products)
    app.UseStaticFiles();

    app.UseApiCors(); // only when Cors:AllowedOrigins is set; before authentication so preflight requests need no token
    app.UseMiddleware<AuthFailureLoggingMiddleware>(); // wraps authentication, authorization and the controllers
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    app.MapControllers();

    // Opt-in (Database:MigrateOnStartup=true, set by docker-compose): create or update the schema before seeding.
    // Off by default so a production database is never changed by simply starting the app.
    if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    // Seed the fixed set of roles used by the application
    await RoleSeeder.SeedRolesAsync(app.Services);
    // Default Admin for demos; credentials come from configuration (user-secrets / env), never from code
    await RoleSeeder.SeedAdminAsync(app.Services);

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException && ex.GetType().Name != "StopTheHostException")
{
    // HostAbortedException is how "dotnet ef" stops the app after reading the model; it is not a failure
    Log.Fatal(ex, "The application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush(); // writes out whatever the file sink still holds
}

// Exposes the entry point to WebApplicationFactory<Program> for integration tests
public partial class Program
{
}
