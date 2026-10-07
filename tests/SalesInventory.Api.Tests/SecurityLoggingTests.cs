using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using Serilog.Events;
using SalesInventory.Api.Security;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Api.Tests;

// The security log of authentication and authorization failures (401 / 403): what it says, what it must never say, and when it
// raises the alarm.
public class SecurityLoggingTests : IClassFixture<LoggingTests.EventFactory>
{
    private readonly LoggingTests.EventFactory _factory;

    public SecurityLoggingTests(LoggingTests.EventFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AuthFailure_NoTokenSent_IsLoggedAsMissingCredentialsWithTheContextToSpotAProbe()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/customers?probe=nomarker1");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var entry = Failures().Last(e => Prop(e, "RequestPath") == "/api/customers");
        Assert.Equal(LogEventLevel.Warning, entry.Level);
        Assert.Equal("AuthFailure", Prop(entry, "SecurityEvent"));
        Assert.Equal("MissingCredentials", Prop(entry, "Reason"));
        Assert.Equal("401", Prop(entry, "StatusCode"));
        Assert.Equal("GET", Prop(entry, "RequestMethod"));
        Assert.Equal("anonymous", Prop(entry, "UserId"));
        Assert.Equal("False", Prop(entry, "TokenSent"));
        Assert.DoesNotContain("nomarker1", Everything(entry)); // the query string is not logged
    }

    [Fact]
    public async Task AuthFailure_ABadBearerToken_IsLoggedAsInvalidTokenWithoutTheTokenItself()
    {
        // Arrange: a made-up token, as a scanner or a stale browser tab would send
        const string token = "garbage.token-VALUE-0123456789.signature";
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/dashboard/summary");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var entry = Failures().Last(e => Prop(e, "RequestPath") == "/api/dashboard/summary");
        Assert.Equal("InvalidOrExpiredToken", Prop(entry, "Reason"));
        Assert.Equal("True", Prop(entry, "TokenSent")); // it says a token was sent, not which
        Assert.DoesNotContain("garbage", Everything(entry));
        Assert.DoesNotContain("0123456789", Everything(entry));
        Assert.DoesNotContain("Bearer", Everything(entry));
    }

    [Fact]
    public async Task AuthFailure_ASignedInUserWithoutTheRole_IsLoggedAsForbiddenWithTheirUserId()
    {
        // Arrange: a seller tries an admin-only endpoint
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "BanHang");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var userId = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.First(c => c.Type is ClaimTypes.NameIdentifier or "nameid" or "sub").Value;

        // Act
        var response = await client.GetAsync("/api/admin/users");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var entry = Failures().Last(e => Prop(e, "RequestPath") == "/api/admin/users");
        Assert.Equal("Forbidden", Prop(entry, "Reason"));
        Assert.Equal("403", Prop(entry, "StatusCode"));
        Assert.Equal(userId, Prop(entry, "UserId"));
        Assert.DoesNotContain(token, Everything(entry));
    }

    [Fact]
    public async Task AuthFailure_AWrongPasswordAtLogin_IsLoggedAsLoginFailedWithoutTheEmailOrThePassword()
    {
        // Arrange
        var client = _factory.CreateClient();
        const string email = "someone.private.logintest@test.local";
        const string password = "Wr0ng-Passw0rd-VALUE";

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginDto { Email = email, Password = password });

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var entry = Failures().Last(e => Prop(e, "RequestPath") == "/api/auth/login");
        Assert.Equal("LoginFailed", Prop(entry, "Reason"));
        Assert.Equal("POST", Prop(entry, "RequestMethod"));
        Assert.DoesNotContain("someone.private", Everything(entry));
        Assert.DoesNotContain("Wr0ng", Everything(entry));
    }

    [Fact]
    public async Task AuthFailure_SuccessfulAndOrdinaryErrorResponses_AreNotSecurityEvents()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "BanHang");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var before = Failures().Count();

        // Act: a 200, a 404 and a 400
        await client.GetAsync("/api/auth/me");
        await client.GetAsync("/api/products/987654321");
        await client.GetAsync("/api/products/search?page=0");

        // Assert: none of them is an authentication or authorization failure
        Assert.Equal(before, Failures().Count());
    }

    [Fact]
    public async Task AuthFailure_ALineBreakInThePath_CannotForgeASecondLogLine()
    {
        // Arrange: a protected route whose {id} holds an encoded CR LF followed by text that looks like a log line
        var client = _factory.CreateClient();

        // Act
        await client.GetAsync("/api/products/1%0d%0a[12:00:00 INF] Admin logged in");

        // Assert: the control characters are gone from the logged path
        var entry = Failures().Last(e => (Prop(e, "RequestPath") ?? "").StartsWith("/api/products/1"));
        var path = Prop(entry, "RequestPath")!;
        Assert.DoesNotContain('\r', path);
        Assert.DoesNotContain('\n', path);
        Assert.DoesNotContain('\r', entry.RenderMessage());
        Assert.DoesNotContain('\n', entry.RenderMessage());
    }

    // ---- the alarm: many failures from one address ----

    [Fact]
    public async Task Alert_RepeatedFailuresFromOneAddress_RaiseAnErrorAtTheThresholdAndAgainAtEveryMultiple()
    {
        // Arrange: a host that alerts at 3 failures
        using var factory = new AlertFactory();
        var client = factory.CreateClient();

        // Act + Assert: one and two failures: only warnings
        await client.GetAsync("/api/customers");
        await client.GetAsync("/api/customers");
        Assert.Empty(Alerts(factory));

        // the third reaches the threshold
        await client.GetAsync("/api/customers");
        var alert = Assert.Single(Alerts(factory));
        Assert.Equal(LogEventLevel.Error, alert.Level);
        Assert.Equal("3", Prop(alert, "FailureCount"));
        Assert.Equal("5", Prop(alert, "WindowMinutes"));

        // and again at the sixth
        await client.GetAsync("/api/customers");
        await client.GetAsync("/api/customers");
        await client.GetAsync("/api/customers");
        Assert.Equal(new[] { "3", "6" }, Alerts(factory).Select(a => Prop(a, "FailureCount")));
    }

    // ---- the counter on its own ----

    [Fact]
    public void Tracker_FailuresOlderThanTheWindow_NoLongerCount()
    {
        // Arrange
        var clock = new ManualClock();
        var tracker = NewTracker(clock, windowMinutes: 5);

        // Act
        var first = tracker.Record("203.0.113.9");
        var second = tracker.Record("203.0.113.9");
        clock.Advance(TimeSpan.FromMinutes(4));
        var third = tracker.Record("203.0.113.9");
        clock.Advance(TimeSpan.FromMinutes(2)); // the first two are now 6 minutes old, the third 2
        var fourth = tracker.Record("203.0.113.9");

        // Assert
        Assert.Equal(new[] { 1, 2, 3, 2 }, new[] { first, second, third, fourth });
    }

    [Fact]
    public void Tracker_EachAddressHasItsOwnCount()
    {
        // Arrange
        var tracker = NewTracker(new ManualClock(), windowMinutes: 5);

        // Act
        tracker.Record("198.51.100.1");
        tracker.Record("198.51.100.1");
        var other = tracker.Record("198.51.100.2");

        // Assert
        Assert.Equal(1, other);
    }

    [Fact]
    public void Tracker_TooManyAddresses_DoesNotGrowWithoutLimit()
    {
        // Arrange: an attacker rotating through far more addresses than the table holds
        var tracker = NewTracker(new ManualClock(), windowMinutes: 5);

        // Act: the call never fails and keeps answering (an address past the limit is simply not counted)
        var last = 0;
        for (var i = 0; i < 12_000; i++)
        {
            last = tracker.Record($"10.{i / 65536}.{i / 256 % 256}.{i % 256}");
        }

        // Assert
        Assert.Equal(1, last);
    }

    // ---- helpers ----

    private IEnumerable<LogEvent> Failures() => _factory.Events.Where(e => Prop(e, "SecurityEvent") == "AuthFailure");

    private static IEnumerable<LogEvent> Alerts(LoggingTests.EventFactory factory) => factory.Events.Where(e => Prop(e, "SecurityEvent") == "SuspiciousActivity");

    private static string? Prop(LogEvent e, string name) =>
        e.Properties.TryGetValue(name, out var value) ? (value is ScalarValue s ? Convert.ToString(s.Value, System.Globalization.CultureInfo.InvariantCulture) : value.ToString()) : null;

    // The message and every property of an entry as one text, to check that nothing sensitive is anywhere in it
    private static string Everything(LogEvent e) => e.RenderMessage() + " " + string.Join(" ", e.Properties.Select(p => p.Key + "=" + p.Value));

    private static AuthFailureTracker NewTracker(TimeProvider clock, int windowMinutes) =>
        new(Options.Create(new AuthFailureOptions { WindowMinutes = windowMinutes }), clock);

    // A clock the test moves by hand
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    // Alerts at 3 failures instead of 10
    private sealed class AlertFactory : LoggingTests.EventFactory
    {
        protected override void ConfigureExtraSettings(IWebHostBuilder builder)
        {
            builder.UseSetting("Security:AuthFailures:AlertThreshold", "3");
            builder.UseSetting("Security:AuthFailures:WindowMinutes", "5");
        }
    }
}
