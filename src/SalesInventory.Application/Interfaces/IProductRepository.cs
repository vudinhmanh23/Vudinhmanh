using SalesInventory.Application.Dtos;
using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Interfaces;

// Whole-catalogue numbers for the dashboard; money is decimal end to end
public record InventorySummary(int TotalProducts, int ActiveProducts, int LowStockProducts, decimal InventoryValue);

public interface IProductRepository : IRepository<Product>
{
    // Products at or below their reorder level (reorder level > 0), biggest shortage first.
    // Inactive products are left out unless includeInactive is true.
    // Filtering and ordering happen in SQL; the returned entities are not tracked (read-only).
    Task<IReadOnlyList<Product>> GetBelowReorderLevelAsync(bool includeInactive = false);

    // Single-row and filtered reads done in SQL (read-only, not tracked); Sku is matched case-insensitively by the column collation
    Task<Product?> GetBySkuAsync(string sku);
    Task<IReadOnlyList<Product>> GetInactiveAsync();
    Task<IReadOnlyList<Product>> GetByCategoryAsync(int categoryId);

    // The products with these ids, tracked, in ONE query (for code that is about to change their stock)
    Task<IReadOnlyList<Product>> GetByIdsAsync(IReadOnlyCollection<int> ids);

    // Which of these ids exist, without loading the products
    Task<IReadOnlyList<int>> GetExistingIdsAsync(IReadOnlyCollection<int> ids);

    // Counts and SUM(StockQuantity * PurchasePrice) computed by the database in a single read-only pass
    Task<InventorySummary> GetInventorySummaryAsync();

    // One page of products matching the optional name search and category filter, plus the total match count.
    // sortBy is "name" or "price" (sale price); filtering, ordering and paging all happen in SQL (read-only).
    Task<(IReadOnlyList<Product> Items, int TotalCount)> SearchAsync(
        string? search, int? categoryId, string sortBy, bool descending, int page, int pageSize);

    // One page of compact product rows for GET /api/products: optional keyword/category/price filters,
    // whitelisted sorting and paging, all in SQL and projected straight to the DTO (read-only).
    Task<(IReadOnlyList<ProductListItemDto> Items, int TotalCount)> QueryAsync(ProductQueryParameters query);
}
