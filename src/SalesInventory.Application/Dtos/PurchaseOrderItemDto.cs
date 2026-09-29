namespace SalesInventory.Application.Dtos;

// Read model for a purchase order line item
public class PurchaseOrderItemDto
{
    public int ProductId { get; set; }
    public string? ProductName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
