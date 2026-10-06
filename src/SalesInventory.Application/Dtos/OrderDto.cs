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

    // Same notices as structured data: the products left at or below their LowStockThreshold
    public List<LowStockProductDto> LowStockProducts { get; set; } = new();
}

public class LowStockProductDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
    public int LowStockThreshold { get; set; }
}
