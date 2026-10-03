namespace SalesInventory.Application.Dtos;

// Write model for creating a purchase order (stock-in) with its line items (TotalAmount is computed on the server)
public class CreatePurchaseOrderDto
{
    public DateTime OrderDate { get; set; }
    public int SupplierId { get; set; }
    public string? Note { get; set; }
    public List<CreatePurchaseOrderItemDto> Items { get; set; } = new();
}
