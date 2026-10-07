namespace SalesInventory.Application.Dtos;

// Compact row for the paged product list: only what a list screen needs, never the full entity
public class ProductListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal SalePrice { get; set; }
    public int Quantity { get; set; }
    public bool IsActive { get; set; }
    public string? ImageUrl { get; set; }
    public int CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? SupplierName { get; set; }
}
