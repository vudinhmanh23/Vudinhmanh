namespace SalesInventory.Web.Models;

/// <summary>Body of POST /api/products and PUT /api/products/{id}. PurchasePrice null on PUT keeps the stored cost price.</summary>
public record ProductWriteRequest(
    string Name,
    string Sku,
    string? Description,
    string? Barcode,
    string Unit,
    decimal? PurchasePrice,
    decimal SalePrice,
    int Quantity,
    int LowStockThreshold,
    int ReorderLevel,
    bool IsActive,
    int CategoryId);
