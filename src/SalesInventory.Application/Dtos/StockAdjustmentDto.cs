namespace SalesInventory.Application.Dtos;

// Result of a manual stock adjustment
public class StockAdjustmentDto
{
    public int ProductId { get; set; }
    public int PreviousQuantity { get; set; }
    public int NewQuantity { get; set; }
    public StockMovementDto Movement { get; set; } = new();
}
