namespace SalesInventory.Api.Dtos;

// Read model returned by purchase order endpoints
public class PurchaseOrderDto
{
    public int Id { get; set; }
    public DateTime OrderDate { get; set; }
    public int SupplierId { get; set; }
    public List<PurchaseOrderItemDto> Items { get; set; } = new();
}
