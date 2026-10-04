namespace SalesInventory.Application.Dtos;

// Write model for creating a product (validated by CreateProductDtoValidator)
public class CreateProductDto
{
    public string Name { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Barcode { get; set; }

    public string Unit { get; set; } = "cái";

    public decimal PurchasePrice { get; set; }

    public decimal SalePrice { get; set; }

    public int Quantity { get; set; }

    public bool IsActive { get; set; } = true;

    public int CategoryId { get; set; }
}
