namespace SalesInventory.Application.Dtos;

// Query-string parameters of GET /api/products. Every filter is optional: it is applied only when it has a value.
public class ProductQueryParameters
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    // Case-insensitive search in name, SKU (product code) and description
    public string? Keyword { get; set; }

    public int? CategoryId { get; set; }

    // Inclusive bounds on the sale price
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }

    // When true only products with StockQuantity > 0 are returned; false/omitted applies no stock filter
    public bool InStockOnly { get; set; }

    // One or more comma-separated keys, applied in order (e.g. "category,price").
    // Allowed keys: name | price | stock | category (case-insensitive); unknown keys are ignored, and with
    // no valid key the list is sorted by name
    public string? SortBy { get; set; }

    // Direction applied to every key in SortBy
    public bool SortDescending { get; set; }

    // 1-based page number; values below 1 are treated as 1
    public int Page
    {
        get => _page;
        set => _page = Math.Max(1, value);
    }

    // Defaults to 20; clamped to 1..100
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, 1, MaxPageSize);
    }
}
