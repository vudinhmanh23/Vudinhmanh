using System.Net;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;

namespace SalesInventory.Api.Hardening;

// Security settings that only tighten PRODUCTION. In Development nothing here changes behaviour (no HSTS, no forwarded headers,
// no safety-net error handler, no startup checks on secrets), so local work stays as it was. The one thing that applies in every
// environment is CORS, and only when origins are configured.
public static class ApiHardeningExtensions
{
    // Services and start-up checks
    public static WebApplicationBuilder AddApiHardening(this WebApplicationBuilder builder)
    {
        var production = !builder.Environment.IsDevelopment();

        // CORS: exact origins only. The origins are read when the policy is first needed (the configuration is complete by then) and
        // validated, so a bad value stops the app; none configured = no browser origin is allowed.
        builder.Services.AddCors();
        builder.Services.AddOptions<CorsOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            var origins = CorsSettings.ReadAllowedOrigins(configuration, requireHttps: production);
            if (origins.Length == 0)
            {
                return;
            }

            options.AddPolicy(CorsSettings.PolicyName, policy => policy
                .WithOrigins(origins)
                .WithMethods("GET", "POST", "PUT", "DELETE")
                .WithHeaders("Authorization", "Content-Type", "Accept")
                // Headers the client reads: the total of a paged list, the wait of a 429, the name of a downloaded file
                .WithExposedHeaders("X-Total-Count", "Retry-After", "Content-Disposition")
                .SetPreflightMaxAge(TimeSpan.FromHours(1)));
            // No AllowCredentials(): the API authenticates with a bearer token in the Authorization header, not with cookies, so a
            // browser never needs to send credentials. Without it, the "any origin + credentials" combination cannot happen.
        });

        if (!production)
        {
            return builder;
        }

        builder.Services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = true;
        });

        // 308 keeps the method and body of a redirected POST (the default 307 is "temporary")
        builder.Services.AddHttpsRedirection(options => options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect);

        // The generic error body (ProblemDetails) of the safety-net handler: never contains exception details outside Development
        builder.Services.AddProblemDetails();

        // Behind a reverse proxy that ends TLS the API sees plain http. The proxy says what the client really used in the
        // X-Forwarded-* headers, which are believed ONLY from the proxies listed in Hardening:TrustedProxies (addresses or CIDR
        // ranges); anyone else could forge them to fake the scheme or hide their IP address.
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            var trusted = builder.Configuration.GetSection("Hardening:TrustedProxies").Get<string[]>() ?? Array.Empty<string>();
            if (trusted.Any(t => !string.IsNullOrWhiteSpace(t)))
            {
                options.KnownProxies.Clear();
                options.KnownNetworks.Clear();
                foreach (var entry in trusted.Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    AddTrustedProxy(options, entry.Trim());
                }
            }
        });

        return builder;
    }

    // First in the pipeline: the real scheme and client IP of the request, and a last-resort error handler
    public static WebApplication UseApiHardeningFirst(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            return app;
        }

        // Secrets only from the environment, and a fully configured application. Checked here, not when the services are registered,
        // because only now is the configuration complete (every source has been added).
        SecretSettingsGuard.EnsureNoSecretsInSettingsFiles((IConfigurationRoot)app.Configuration);
        SecretSettingsGuard.EnsureRequiredSettings(app.Configuration);

        app.UseForwardedHeaders();

        // Safety net around everything else: GlobalExceptionHandlingMiddleware turns exceptions into ProblemDetails with the right
        // status; this catches whatever escapes it (or the middleware before it) and answers with a generic 500 and no details.
        app.UseExceptionHandler();
        return app;
    }

    // Before the https redirection: tells browsers to use https only, for a year, on this host and its subdomains
    public static WebApplication UseApiHsts(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        return app;
    }

    // Before authentication, so a preflight request (which carries no token) is answered by CORS and not rejected as unauthorized
    public static WebApplication UseApiCors(this WebApplication app)
    {
        var production = !app.Environment.IsDevelopment();
        if (CorsSettings.ReadAllowedOrigins(app.Configuration, requireHttps: production).Length > 0)
        {
            app.UseCors(CorsSettings.PolicyName);
        }

        return app;
    }

    private static void AddTrustedProxy(ForwardedHeadersOptions options, string entry)
    {
        var parts = entry.Split('/');
        if (!IPAddress.TryParse(parts[0], out var address))
        {
            throw new InvalidOperationException($"Hardening:TrustedProxies contains '{entry}', which is not an IP address or a CIDR range.");
        }

        if (parts.Length == 1)
        {
            options.KnownProxies.Add(address);
            return;
        }

        var maxPrefix = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        if (parts.Length != 2 || !int.TryParse(parts[1], out var prefix) || prefix < 0 || prefix > maxPrefix)
        {
            throw new InvalidOperationException($"Hardening:TrustedProxies contains '{entry}', which is not a valid CIDR range.");
        }

        options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(address, prefix));
    }
}
