using SalesInventory.Api.RateLimiting;
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

var builder = WebApplication.CreateBuilder(args);

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
        "Jwt:Key is missing or shorter than 32 bytes. Set it with: dotnet user-secrets set \"Jwt:Key\" \"<random 32+ char secret>\"");
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
app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Serves wwwroot (including uploaded product images under /uploads/products)
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

// Seed the fixed set of roles used by the application
await RoleSeeder.SeedRolesAsync(app.Services);
// Default Admin for demos; credentials come from configuration (user-secrets / env), never from code
await RoleSeeder.SeedAdminAsync(app.Services);

app.Run();

// Exposes the entry point to WebApplicationFactory<Program> for integration tests
public partial class Program
{
}
