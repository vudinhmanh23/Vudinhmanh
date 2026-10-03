namespace SalesInventory.Application.Dtos;

// Read model returned by purchase order endpoints
public class PurchaseOrderDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    // Draft, Approved or Cancelled
    public string Status { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public int SupplierId { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Note { get; set; }
    public List<PurchaseOrderItemDto> Items { get; set; } = new();
}
