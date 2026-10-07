using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SalesInventory.Api.Hardening;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;

namespace SalesInventory.Api.Tests;

// Production hardening: https only (HSTS and redirection, also behind a TLS-terminating proxy), CORS for exactly one browser origin,
// no exception details in error responses, and secrets that may only come from the environment. The Development counterparts at the
// end show that none of this changes how the API behaves while developing.
public class HardeningTests : IClassFixture<HardeningTests.ProductionFactory>
{
    private const string ClientOrigin = "https://app.example.test";

    private readonly ProductionFactory _factory;

    public HardeningTests(ProductionFactory factory)
    {
        _factory = factory;
    }

    // ---- HTTPS: HSTS and redirection ----

    [Fact]
    public async Task Hsts_ProductionHttpsRequest_SendsStrictTransportSecurityForAYear()
    {
        // Arrange
        var client = Https();

        // Act
        var response = await client.GetAsync("/api/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("max-age=31536000; includeSubDomains", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    [Fact]
    public async Task HttpsRedirection_ProductionPlainHttp_AnswersWithAPermanentRedirectToHttpsKeepingPathAndQuery()
    {
        // Arrange
        var client = Http();

        // Act
        var response = await client.GetAsync("/api/health?probe=1");

        // Assert: 308 (the method and body of a POST survive it), to the https address
        Assert.Equal(HttpStatusCode.PermanentRedirect, response.StatusCode);
        Assert.Equal("https://api.example.test/api/health?probe=1", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task ForwardedProto_FromATrustedProxy_IsBelievedSoThereIsNoRedirectLoopAndHstsIsSent()
    {
        // Arrange: the proxy (10.1.2.3, inside the trusted range) ended TLS and talks plain http to the API
        var client = Http();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("X-Test-Remote-Ip", "10.1.2.3");
        request.Headers.Add("X-Forwarded-Proto", "https");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task ForwardedProto_FromAnUntrustedClient_IsIgnoredSoItCannotFakeHttps()
    {
        // Arrange: an ordinary client outside the trusted range claims the connection was https
        var client = Http();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("X-Test-Remote-Ip", "203.0.113.9");
        request.Headers.Add("X-Forwarded-Proto", "https");

        // Act
        var response = await client.SendAsync(request);

        // Assert: still treated as plain http, so redirected
        Assert.Equal(HttpStatusCode.PermanentRedirect, response.StatusCode);
    }

    // ---- CORS ----

    [Fact]
    public async Task Cors_PreflightFromTheBlazorOrigin_IsAllowedForTheNeededMethodAndHeadersAndWithoutCredentials()
    {
        // Arrange
        var request = Preflight(ClientOrigin, "POST", "authorization,content-type");

        // Act
        var response = await Https().SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(ClientOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains("POST", string.Join(",", response.Headers.GetValues("Access-Control-Allow-Methods")));
        Assert.Contains("authorization", string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers")), StringComparison.OrdinalIgnoreCase);
        // Cookies and credentials are not used, so they are not allowed: "any origin + credentials" can never happen
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData("https://evil.example.test")]
    [InlineData("http://app.example.test")]       // same host, wrong scheme
    [InlineData("https://app.example.test:8443")] // same host, wrong port
    [InlineData("https://sub.app.example.test")]  // a subdomain is not the origin
    public async Task Cors_PreflightFromAnyOtherOrigin_GetsNoAllowOriginHeader(string origin)
    {
        // Arrange
        var request = Preflight(origin, "POST", "authorization");

        // Act
        var response = await Https().SendAsync(request);

        // Assert
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Cors_RealRequestFromTheBlazorOrigin_CarriesTheOriginAndExposesTheHeadersTheClientReads()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("Origin", ClientOrigin);

        // Act
        var response = await Https().SendAsync(request);

        // Assert
        Assert.Equal(ClientOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        var exposed = string.Join(",", response.Headers.GetValues("Access-Control-Expose-Headers"));
        Assert.Contains("X-Total-Count", exposed);
        Assert.Contains("Retry-After", exposed);
    }

    [Fact]
    public async Task Cors_RealRequestFromAnotherOrigin_GetsNoAllowOriginHeader()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("Origin", "https://evil.example.test");

        // Act
        var response = await Https().SendAsync(request);

        // Assert: the response is the same, but the browser will not hand it to the other site
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.example.test")]
    [InlineData("https://app.example.test/some/path")]
    [InlineData("ftp://app.example.test")]
    [InlineData("not an origin")]
    [InlineData("http://app.example.test")]  // production requires https
    public void Cors_ABadOriginInProduction_StopsTheApplicationAtStartup(string origin)
    {
        // Arrange
        using var factory = new ProductionFactory(b => b.UseSetting("Cors:AllowedOrigins:0", origin));

        // Act
        var failure = Record.Exception(() => factory.CreateClient());

        // Assert
        Assert.Contains(Messages(failure), m => m.Contains("Cors:AllowedOrigins"));
    }

    // ---- errors in production ----

    [Fact]
    public async Task Errors_ProductionServerFault_AnswersWithAGenericProblemAndNoExceptionDetails()
    {
        // Arrange: a signed-in user, and a service that fails with an internal message
        var client = Https();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "BanHang");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.PostAsJsonAsync("/api/assistant/ask", new AskRequestDto { Question = "Xin chào" });

        // Assert: 500, problem+json, a trace id to quote to support, and nothing about what went wrong inside
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("traceId", body);
        Assert.DoesNotContain("SECRET-INTERNAL-DETAIL", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("   at ", body); // no stack trace
        Assert.DoesNotContain("SalesInventory.", body);
    }

    [Fact]
    public async Task Errors_AnExceptionThatEscapesTheNormalHandler_StillGetsAGenericProblemFromTheSafetyNet()
    {
        // Arrange: a small app with only the first hardening step, followed by a middleware that blows up
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = "Server=prod;" });
        builder.AddApiHardening();
        await using var app = builder.Build();
        app.UseApiHardeningFirst();
        app.Run(_ => throw new InvalidOperationException("SECRET-OUTSIDE-DETAIL"));
        await app.StartAsync();

        // Act
        var response = await app.GetTestClient().GetAsync("/anything");

        // Assert
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.DoesNotContain("SECRET-OUTSIDE-DETAIL", body);
        Assert.DoesNotContain("InvalidOperationException", body);
    }

    // ---- secrets only from the environment ----

    [Theory]
    [InlineData("Jwt", "Key", "0123456789abcdef0123456789abcdef-from-a-file")]
    [InlineData("ConnectionStrings", "DefaultConnection", "Server=file;Database=x;Password=from-a-file")]
    [InlineData("Anthropic", "ApiKey", "sk-ant-from-a-file")]
    [InlineData("SeedAdmin", "Password", "FromAFile123")]
    public void Secrets_ProductionSettingsFileHoldingASecret_StopsTheApplicationAndNamesTheSetting(string section, string key, string value)
    {
        // Arrange: a settings file next to appsettings.json (as appsettings.Production.json would be) with a secret in it
        var file = Path.Combine(Path.GetTempPath(), $"hardening-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new Dictionary<string, object> { [section] = new Dictionary<string, string> { [key] = value } }));
        try
        {
            using var factory = new ProductionFactory(b => b.ConfigureAppConfiguration((_, config) => config.AddJsonFile(file, optional: false)));

            // Act
            var failure = Record.Exception(() => factory.CreateClient());

            // Assert
            Assert.Contains(Messages(failure), m => m.Contains("Secrets found in a settings file") && m.Contains($"{section}:{key}"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Secrets_ProductionWithoutAConnectionString_StopsTheApplicationAndSaysWhichVariableToSet()
    {
        // Arrange: this host overrides the connection string with an empty one
        using var factory = new ProductionFactory(b => b.UseSetting("ConnectionStrings:DefaultConnection", ""));

        // Act
        var failure = Record.Exception(() => factory.CreateClient());

        // Assert
        Assert.Contains(Messages(failure), m => m.Contains("ConnectionStrings__DefaultConnection"));
    }

    [Fact]
    public async Task Secrets_ValuesFromTheEnvironmentStyleSources_AreAccepted()
    {
        // Arrange: ProductionFactory gives the connection string and the JWT key as plain settings, not from a file: this is what
        // environment variables look like to the application. The host starts, and serves.
        var client = Https();

        // Act
        var response = await client.GetAsync("/api/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Secrets_TheSettingsFilesInTheRepository_HoldNoSecretValues()
    {
        // Arrange: every appsettings*.json under src
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(Path.Combine(root, "src"), "appsettings*.json", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        var secretName = new Regex(@"(api_?key|secret|password|pwd|connectionstring|:key$)", RegexOptions.IgnoreCase);

        // Act: every text value whose name says "secret"
        var leaks = new List<string>();
        foreach (var file in files)
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            foreach (var (path, value) in Leaves(json.RootElement, ""))
            {
                if ((secretName.IsMatch(path) || path.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(value))
                {
                    leaks.Add($"{Path.GetFileName(file)}: {path}");
                }
            }
        }

        // Assert
        Assert.NotEmpty(files);
        Assert.Empty(leaks);
    }

    // ---- helpers ----

    private HttpClient Https() => _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://api.example.test"), AllowAutoRedirect = false });

    private HttpClient Http() => _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://api.example.test"), AllowAutoRedirect = false });

    private static HttpRequestMessage Preflight(string origin, string method, string headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/products");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", headers);
        return request;
    }

    private static IEnumerable<string> Messages(Exception? exception)
    {
        Assert.NotNull(exception);
        for (var e = exception; e is not null; e = e.InnerException)
        {
            yield return e.Message;
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SalesInventory.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("SalesInventory.sln not found above the test binaries.");
    }

    private static IEnumerable<(string Path, string? Value)> Leaves(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var leaf in Leaves(property.Value, path.Length == 0 ? property.Name : path + ":" + property.Name))
                    {
                        yield return leaf;
                    }
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var leaf in Leaves(item, path + ":" + index++))
                    {
                        yield return leaf;
                    }
                }

                break;
            case JsonValueKind.String:
                yield return (path, element.GetString());
                break;
        }
    }

    // A production host: allowed Blazor origin, a trusted proxy range, https port 443, the database and JWT key as plain settings
    // (in real life environment variables), and a service that fails with an internal message
    public class ProductionFactory : CustomWebApplicationFactory
    {
        private readonly Action<IWebHostBuilder>? _customize;

        public ProductionFactory()
        {
        }

        internal ProductionFactory(Action<IWebHostBuilder> customize)
        {
            _customize = customize;
        }

        protected override void ConfigureExtraSettings(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=prod-db;Database=Shop;User Id=app;Password=not-used-by-tests;");
            builder.UseSetting("HTTPS_PORT", "443");
            builder.UseSetting("AiSafety:RateLimit:PermitLimit", "1000");
            builder.UseSetting("Cors:AllowedOrigins:0", ClientOrigin);
            builder.UseSetting("Hardening:TrustedProxies:0", "10.0.0.0/8");
            _customize?.Invoke(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddTransient<IStartupFilter, FakeRemoteIpFilter>();
                services.AddScoped<IChatService>(_ => throw new InvalidOperationException("SECRET-INTERNAL-DETAIL"));
            });
        }
    }

    // TestServer has no network peer. This lets a test say which address the request "came from" (header X-Test-Remote-Ip), so the
    // trusted-proxy rules can be tried. It runs before the application's own pipeline.
    private sealed class FakeRemoteIpFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-Remote-Ip", out var ip))
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}

// The same API in Development: the hardening must not change what developers see
public class HardeningDevelopmentTests : IClassFixture<HardeningDevelopmentTests.DevelopmentFactory>
{
    private readonly DevelopmentFactory _factory;

    public HardeningDevelopmentTests(DevelopmentFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Development_NoHstsHeaderIsSent()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://api.example.test"), AllowAutoRedirect = false });

        // Act
        var response = await client.GetAsync("/api/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Development_WithoutConfiguredOrigins_NoCorsHeadersAreSent()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("Origin", "https://app.example.test");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Development_ServerFault_StillShowsTheExceptionDetailsToTheDeveloper()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "BanHang");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.PostAsJsonAsync("/api/assistant/ask", new AskRequestDto { Question = "Xin chào" });

        // Assert: unchanged: a developer sees what went wrong
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("SECRET-DEV-DETAIL", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Development_AnInvalidCorsOriginIsStillRefused()
    {
        // Arrange: the origin check applies in every environment (a wildcard is wrong everywhere)
        using var factory = new DevelopmentFactory(b => b.UseSetting("Cors:AllowedOrigins:0", "*"));

        // Act
        var failure = Record.Exception(() => factory.CreateClient());

        // Assert
        Assert.NotNull(failure);
    }

    public class DevelopmentFactory : CustomWebApplicationFactory
    {
        private readonly Action<IWebHostBuilder>? _customize;

        public DevelopmentFactory()
        {
        }

        internal DevelopmentFactory(Action<IWebHostBuilder> customize)
        {
            _customize = customize;
        }

        protected override void ConfigureExtraSettings(IWebHostBuilder builder)
        {
            builder.UseSetting("AiSafety:RateLimit:PermitLimit", "1000");
            _customize?.Invoke(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
                services.AddScoped<IChatService>(_ => throw new InvalidOperationException("SECRET-DEV-DETAIL")));
        }
    }
}
