namespace SalesInventory.Application.Dtos;

// Read model returned by sales order endpoints
public class OrderDto
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public int CustomerId { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Note { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();

    // Low-stock notices raised by this sale (only filled on create); they never block the order
    public List<string> Warnings { get; set; } = new();
}
