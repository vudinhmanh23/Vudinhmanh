namespace SalesInventory.Web.Models;

/// <summary>Subset of the API's ProductDto that the product list displays.</summary>
public record ProductItem(
    int Id,
    string Name,
    string Sku,
    string Unit,
    decimal SalePrice,
    int Quantity,
    bool IsActive,
    string? CategoryName,
    string? SupplierName,
    string? ImageUrl = null);
