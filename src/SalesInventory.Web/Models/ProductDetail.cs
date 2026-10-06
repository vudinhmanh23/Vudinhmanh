namespace SalesInventory.Web.Models;

/// <summary>Full product as returned by GET /api/products/{id}. The API never returns the cost price.</summary>
public record ProductDetail(
    int Id,
    string Name,
    string Sku,
    string? Description,
    string? Barcode,
    string Unit,
    decimal SalePrice,
    int Quantity,
    int LowStockThreshold,
    int ReorderLevel,
    bool IsActive,
    int CategoryId);
