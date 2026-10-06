using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SalesInventory.Application.Interfaces;
using SalesInventory.Infrastructure.Identity;
using SalesInventory.Infrastructure.Persistence;
using SalesInventory.Infrastructure.Repositories;
using SalesInventory.Infrastructure.Storage;

namespace SalesInventory.Infrastructure;

public static class DependencyInjection
{
    // Registers persistence (EF Core), Identity, JWT token generation and repositories
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        // Identity: users, roles and password policy
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequiredLength = 8;
            options.Password.RequireUppercase = true;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = false;
            options.Password.RequireNonAlphanumeric = false;
        })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        // JWT: options bound from configuration ("Jwt:Key" comes from user-secrets, never committed)
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.AddScoped<ITokenService, TokenService>();

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>();
        services.AddScoped<IStockMovementRepository, StockMovementRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ISalesOrderRepository, SalesOrderRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Product images from the internet: SSRF-safe downloader plus the Open Food Facts barcode lookup
        services.AddSingleton<IRemoteImageDownloader, SafeImageDownloader>();
        services.AddHttpClient<IProductImageLookup, OpenFoodFactsImageLookup>(client =>
        {
            client.BaseAddress = new Uri("https://world.openfoodfacts.org/");
            client.Timeout = TimeSpan.FromSeconds(10);
            // Open Food Facts asks API clients to identify themselves
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SalesInventory/1.0");
        });

        return services;
    }
}
