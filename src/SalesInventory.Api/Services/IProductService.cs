using SalesInventory.Domain.Entities;

namespace SalesInventory.Api.Services;

// Business-facing operations for Product, on top of the repository layer
public interface IProductService
{
    Task<IEnumerable<Product>> GetProductsAsync();
    Task<Product?> GetProductAsync(int id);
    Task<IEnumerable<Product>> GetProductsByCategoryIdAsync(int categoryId);
    Task<Product> CreateProductAsync(Product product);
    Task<bool> UpdateProductAsync(int id, Product product);
    Task<bool> DeleteProductAsync(int id);
}
