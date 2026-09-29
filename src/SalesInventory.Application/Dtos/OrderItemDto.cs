namespace SalesInventory.Application.Dtos;

// Read model for a sales order line item
public class OrderItemDto
{
    public int ProductId { get; set; }
    public string? ProductName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
