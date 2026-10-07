using SalesInventory.Application.Dtos;
using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Interfaces;

// Business-facing operations for Product, on top of the repository layer
public interface IProductService
{
    Task<IEnumerable<Product>> GetProductsAsync();
    Task<Product?> GetProductAsync(int id);
    Task<Product?> GetProductBySkuAsync(string sku);
    Task<IEnumerable<Product>> GetInactiveProductsAsync();

    // Products with ReorderLevel > 0 and StockQuantity <= ReorderLevel, biggest shortage first.
    // Only active products unless includeInactive is true (e.g. for a stocktake).
    Task<IReadOnlyList<Product>> GetLowStockProductsAsync(bool includeInactive = false);

    Task<InventorySummary> GetInventorySummaryAsync();

    // One page of products for list screens; see IProductRepository.SearchAsync for the parameters
    Task<(IReadOnlyList<Product> Items, int TotalCount)> SearchProductsAsync(
        string? search, int? categoryId, string sortBy, bool descending, int page, int pageSize);
    // One page of compact product rows with optional filters and whitelisted sorting
    Task<(IReadOnlyList<ProductListItemDto> Items, int TotalCount)> QueryProductsAsync(ProductQueryParameters query);
    Task<bool> IsSkuTakenAsync(string sku, int? excludeProductId);
    Task<bool> IsBarcodeTakenAsync(string barcode, int? excludeProductId);
    Task<IEnumerable<Product>> GetProductsByCategoryIdAsync(int categoryId);
    Task<Product> CreateProductAsync(Product product);
    Task<bool> UpdateProductAsync(int id, Product product);
    Task<bool> DeleteProductAsync(int id);

    // Sets (or clears, with null) the product image URL. Found is false when the product doesn't exist;
    // PreviousImageUrl lets the caller delete the replaced file.
    Task<(bool Found, string? PreviousImageUrl)> SetProductImageAsync(int id, string? imageUrl);
}
