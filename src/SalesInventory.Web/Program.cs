using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using SalesInventory.Web.Components;
using SalesInventory.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Lets the HttpClient handler reach circuit-scoped services (see CircuitServicesAccessor)
builder.Services.AddScoped<CircuitServicesAccessor>();
builder.Services.AddScoped<CircuitHandler, ServicesAccessorCircuitHandler>();

builder.Services.AddScoped<TokenStore>();

// AuthorizeView / role checks read the user from the JWT; both registrations resolve to the same instance
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<JwtAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<JwtAuthenticationStateProvider>());
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<CatalogApi>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddTransient<AuthMessageHandler>();

// Named client for the backend API; the URL comes from configuration (ApiBaseUrl)
var apiBaseUrl = builder.Configuration["ApiBaseUrl"]
    ?? throw new InvalidOperationException("Missing configuration value 'ApiBaseUrl'.");
builder.Services.AddHttpClient(ApiClient.Name, client => client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<AuthMessageHandler>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();



