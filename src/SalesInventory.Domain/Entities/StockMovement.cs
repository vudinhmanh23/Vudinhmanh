using System.ComponentModel.DataAnnotations;
using SalesInventory.Domain.Enums;

namespace SalesInventory.Domain.Entities;

// Append-only ledger row: every change to Product.StockQuantity is recorded here
public class StockMovement
{
    [Key]
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public StockMovementType MovementType { get; set; }

    // Signed delta: positive for stock in, negative for stock out
    public int Quantity { get; set; }

    // The kind of document that caused the movement, e.g. "PurchaseOrder"
    [Required]
    [MaxLength(50)]
    public string ReferenceType { get; set; } = string.Empty;

    // Id of that document (e.g. PurchaseOrder.Id)
    public int ReferenceId { get; set; }

    // UTC timestamp of the movement
    public DateTime CreatedAt { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }
}
