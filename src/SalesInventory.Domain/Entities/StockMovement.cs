using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Domain.Entities;

// Append-only ledger row: every change to Product.StockQuantity is recorded here
public class StockMovement
{
    [Key]
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    // Signed delta: positive = stock in, negative = stock out
    public int ChangeQuantity { get; set; }

    // What caused the change, e.g. "Purchase"
    [Required]
    [MaxLength(50)]
    public string Reason { get; set; } = string.Empty;

    // Id of the document that caused the change (e.g. PurchaseOrder.Id for Reason = "Purchase")
    public int RefId { get; set; }

    public DateTime CreatedAt { get; set; }
}
