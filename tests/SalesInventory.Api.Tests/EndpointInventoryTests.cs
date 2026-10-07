using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace SalesInventory.Api.Tests;

// Which endpoints exist and who may call them, read from the application's real routing table (not from reading the code), and the
// guarantee that nothing is open to the world except the few endpoints that are meant to be.
public class EndpointInventoryTests : IClassFixture<CustomWebApplicationFactory>
{
    // The endpoints that may be called WITHOUT a token. Anything else that appears here is an endpoint opened by mistake.
    // (Register and login must be open; the two health checks are probes.)
    private static readonly string[] ExpectedOpenEndpoints =
    {
        "GET api/Health",
        "GET api/Health/db",
        "POST api/auth/login",
        "POST api/auth/register"
    };

    private readonly CustomWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public EndpointInventoryTests(CustomWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public sealed record EndpointInfo(string Method, string Route, string Controller, string Action, string ClassAttribute, string MethodAttribute,
        bool Anonymous, bool Open, string Effective, RoutePattern Pattern, string? Consumes);

    [Fact]
    public void Inventory_EveryEndpointIsEitherProtectedOrOnTheListOfDeliberatelyOpenOnes()
    {
        // Arrange + Act: the real routing table
        var endpoints = ReadEndpoints();
        Save(endpoints);

        // Assert: what is open (no [Authorize] at all, or [AllowAnonymous]) is exactly the expected list
        var open = endpoints.Where(e => e.Open).Select(e => $"{e.Method} {e.Route}").OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(ExpectedOpenEndpoints, open);
        Assert.True(endpoints.Count >= 70, $"expected the whole API, found {endpoints.Count} endpoints");
        Assert.DoesNotContain(endpoints, e => e.Anonymous && !ExpectedOpenEndpoints.Contains($"{e.Method} {e.Route}")); // no stray [AllowAnonymous]
    }

    [Fact]
    public async Task Sweep_EveryProtectedEndpointAnswers401ToACallerWithoutAToken()
    {
        // Arrange: every endpoint that should be protected, with sample values for its route parameters
        var client = _factory.CreateClient();
        var protectedEndpoints = ReadEndpoints().Where(e => !e.Open).ToList();

        // Act: call each one with no Authorization header
        var surprises = new List<string>();
        foreach (var endpoint in protectedEndpoints)
        {
            var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), "/" + Fill(endpoint.Pattern));
            if (endpoint.Method is "POST" or "PUT")
            {
                // An endpoint that only accepts one kind of body (the image upload takes multipart/form-data) answers 415 to any other
                // kind BEFORE the authorization check, so each one gets a body of the kind it takes
                request.Content = endpoint.Consumes is { } type && type.StartsWith("multipart/")
                    ? new MultipartFormDataContent()
                    : new StringContent("{}", Encoding.UTF8, "application/json");
            }

            var response = await client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                surprises.Add($"{endpoint.Method} /{endpoint.Route} answered {(int)response.StatusCode}");
            }
        }

        // Assert: none of them gave anything but 401 (not data, not a validation error, not a server error)
        Assert.NotEmpty(protectedEndpoints);
        Assert.Empty(surprises);
    }

    // ---- reading the routing table ----

    private List<EndpointInfo> ReadEndpoints()
    {
        var result = new List<EndpointInfo>();
        var source = _factory.Services.GetRequiredService<EndpointDataSource>();

        foreach (var endpoint in source.Endpoints.OfType<RouteEndpoint>())
        {
            var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            if (action is null)
            {
                continue; // only the API controllers
            }

            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? new[] { "?" };
            var attributes = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            var classAttributes = action.ControllerTypeInfo.GetCustomAttributes(typeof(IAuthorizeData), inherit: true).Cast<IAuthorizeData>().ToList();
            var methodAttributes = action.MethodInfo.GetCustomAttributes(typeof(IAuthorizeData), inherit: true).Cast<IAuthorizeData>().ToList();
            var anonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var open = anonymous || attributes.Count == 0;

            foreach (var method in methods)
            {
                result.Add(new EndpointInfo(
                    method,
                    endpoint.RoutePattern.RawText!.TrimStart('/'),
                    action.ControllerName,
                    action.ActionName,
                    Describe(classAttributes),
                    anonymous ? "[AllowAnonymous]" : Describe(methodAttributes),
                    anonymous,
                    open,
                    open ? "OPEN (no token needed)" : Effective(attributes),
                    endpoint.RoutePattern,
                    endpoint.Metadata.GetMetadata<IAcceptsMetadata>()?.ContentTypes.FirstOrDefault()));
            }
        }

        return result.OrderBy(e => e.Controller, StringComparer.Ordinal).ThenBy(e => e.Route, StringComparer.Ordinal).ThenBy(e => e.Method, StringComparer.Ordinal).ToList();
    }

    private static string Describe(IEnumerable<IAuthorizeData> attributes)
    {
        var parts = attributes.Select(a => !string.IsNullOrEmpty(a.Policy) ? $"Policy={a.Policy}" : !string.IsNullOrEmpty(a.Roles) ? $"Roles={a.Roles}" : "any signed-in user").ToList();
        return parts.Count == 0 ? "-" : "[Authorize] " + string.Join(" + ", parts);
    }

    // Several [Authorize] attributes on one endpoint (class + method) ALL have to pass: roles are intersected, policies are added up
    private static string Effective(IReadOnlyList<IAuthorizeData> attributes)
    {
        IEnumerable<string>? roles = null;
        var policies = new List<string>();
        foreach (var attribute in attributes)
        {
            if (!string.IsNullOrEmpty(attribute.Policy))
            {
                policies.Add(attribute.Policy);
            }

            if (!string.IsNullOrEmpty(attribute.Roles))
            {
                var set = attribute.Roles.Split(',').Select(r => r.Trim()).ToList();
                roles = roles is null ? set : roles.Intersect(set).ToList();
            }
        }

        var parts = new List<string>();
        if (roles is not null)
        {
            parts.Add("Roles " + string.Join("/", roles));
        }

        parts.AddRange(policies.Select(p => "Policy " + p));
        return parts.Count == 0 ? "any signed-in user" : string.Join(" + ", parts);
    }

    // The URL of an endpoint with a sample value for every route parameter
    private static string Fill(RoutePattern pattern)
    {
        var path = pattern.RawText!.TrimStart('/');
        foreach (var parameter in pattern.Parameters)
        {
            var isGuid = parameter.ParameterPolicies.Any(p => p.Content == "guid");
            path = Regex.Replace(path, @"\{\*?" + parameter.Name + @"(:[^}]*)?\??\}", isGuid ? Guid.NewGuid().ToString() : "1");
        }

        return path;
    }

    // A readable copy of the table for people (also what the project documentation is made from)
    private void Save(IReadOnlyList<EndpointInfo> endpoints)
    {
        var text = new StringBuilder();
        text.AppendLine("| Method | Route | Class attribute | Method attribute | Effective access |");
        text.AppendLine("|---|---|---|---|---|");
        foreach (var e in endpoints)
        {
            text.AppendLine($"| {e.Method} | /{e.Route} | {e.ClassAttribute} | {e.MethodAttribute} | {e.Effective} |");
        }

        var folder = Path.Combine(Path.GetTempPath(), "salesinventory-tests");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "api-endpoints.md"), text.ToString());
        _output.WriteLine($"{endpoints.Count} endpoints; open ones: {string.Join(", ", endpoints.Where(x => x.Open).Select(x => x.Method + " /" + x.Route))}");
    }
}
