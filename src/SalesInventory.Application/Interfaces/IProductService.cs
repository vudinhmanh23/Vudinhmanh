using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Interfaces;

// Business-facing operations for Product, on top of the repository layer
public interface IProductService
{
    Task<IEnumerable<Product>> GetProductsAsync();
    Task<Product?> GetProductAsync(int id);
    Task<Product?> GetProductBySkuAsync(string sku);
    Task<IEnumerable<Product>> GetInactiveProductsAsync();
    Task<bool> IsSkuTakenAsync(string sku, int? excludeProductId);
    Task<bool> IsBarcodeTakenAsync(string barcode, int? excludeProductId);
    Task<IEnumerable<Product>> GetProductsByCategoryIdAsync(int categoryId);
    Task<Product> CreateProductAsync(Product product);
    Task<bool> UpdateProductAsync(int id, Product product);
    Task<bool> DeleteProductAsync(int id);
}
