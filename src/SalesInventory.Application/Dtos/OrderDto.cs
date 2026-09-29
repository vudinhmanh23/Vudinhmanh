namespace SalesInventory.Application.Dtos;

// Read model returned by sales order endpoints
public class OrderDto
{
    public int Id { get; set; }
    public DateTime OrderDate { get; set; }
    public int CustomerId { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}
