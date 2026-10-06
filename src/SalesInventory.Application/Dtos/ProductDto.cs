namespace SalesInventory.Application.Dtos;

// Read model returned by product endpoints
public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Barcode { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal SalePrice { get; set; }
    public int Quantity { get; set; }
    public int LowStockThreshold { get; set; }
    public int ReorderLevel { get; set; }
    public bool IsActive { get; set; }
    public int CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public int? SupplierId { get; set; }
    public string? SupplierName { get; set; }
}
