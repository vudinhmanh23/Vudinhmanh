using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SalesInventory.Application.Interfaces;
using SalesInventory.Infrastructure.Identity;
using SalesInventory.Infrastructure.Persistence;
using SalesInventory.Infrastructure.Repositories;

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

        return services;
    }
}
