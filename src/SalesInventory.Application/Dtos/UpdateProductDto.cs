namespace SalesInventory.Application.Dtos;

// Write model for updating an existing product (validated by UpdateProductDtoValidator)
public class UpdateProductDto
{
    public string Name { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Barcode { get; set; }

    public string Unit { get; set; } = "cái";

    public decimal PurchasePrice { get; set; }

    public decimal SalePrice { get; set; }

    public int Quantity { get; set; }

    public int LowStockThreshold { get; set; } = 5;

    public bool IsActive { get; set; } = true;

    public int CategoryId { get; set; }
}
