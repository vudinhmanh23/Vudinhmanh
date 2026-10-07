using Microsoft.AspNetCore.Components.Server.Circuits;

namespace SalesInventory.Web.Services;

/// <summary>
/// Exposes the current Blazor circuit's service provider to code that runs in another DI scope.
/// HttpClient message handlers are created in their own scope by IHttpClientFactory, so they cannot
/// receive circuit-scoped services (such as the JS-backed session storage) through constructor injection.
/// </summary>
public class CircuitServicesAccessor
{
    private static readonly AsyncLocal<IServiceProvider?> BlazorServices = new();

    public IServiceProvider? Services
    {
        get => BlazorServices.Value;
        set => BlazorServices.Value = value;
    }
}

/// <summary>Publishes the circuit's service provider to <see cref="CircuitServicesAccessor"/> while circuit code runs.</summary>
public class ServicesAccessorCircuitHandler : CircuitHandler
{
    private readonly IServiceProvider _services;
    private readonly CircuitServicesAccessor _accessor;

    public ServicesAccessorCircuitHandler(IServiceProvider services, CircuitServicesAccessor accessor)
    {
        _services = services;
        _accessor = accessor;
    }

    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next)
    {
        return async context =>
        {
            _accessor.Services = _services;
            await next(context);
            _accessor.Services = null;
        };
    }
}
