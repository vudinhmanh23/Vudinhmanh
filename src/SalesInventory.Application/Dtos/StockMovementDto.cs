namespace SalesInventory.Application.Dtos;

// Read model for one stock ledger row
public class StockMovementDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }

    // Purchase, Sale or Adjustment
    public string MovementType { get; set; } = string.Empty;

    // Signed: positive for stock in, negative for stock out
    public int Quantity { get; set; }
    public string RefType { get; set; } = string.Empty;
    public int RefId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Note { get; set; }
}
