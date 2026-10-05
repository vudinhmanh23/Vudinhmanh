using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SalesInventory.Application.Interfaces;
using SalesInventory.Application.Services;

namespace SalesInventory.Application;

public static class DependencyInjection
{
    // Registers the business-service layer; repositories are supplied by the Infrastructure layer
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddAutoMapper(typeof(DependencyInjection).Assembly);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
        services.AddScoped<IStockMovementService, StockMovementService>();
        services.AddScoped<ISalesOrderService, SalesOrderService>();

        return services;
    }
}
