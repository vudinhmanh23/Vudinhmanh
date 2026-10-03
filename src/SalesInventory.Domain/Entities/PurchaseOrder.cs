using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Domain.Entities;

// Represents a purchase order placed with a supplier to restock inventory
public class PurchaseOrder
{
    [Key]
    public int Id { get; set; }

    // Human-readable document number, e.g. PO-20260826-001 (generated on the server)
    [Required]
    [MaxLength(30)]
    public string Code { get; set; } = string.Empty;

    [Required]
    public DateTime OrderDate { get; set; }

    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    // Sum of all line totals; always computed on the server
    public decimal TotalAmount { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    // Line items belonging to this purchase order
    public ICollection<PurchaseOrderItem> PurchaseOrderItems { get; set; } = new List<PurchaseOrderItem>();
}
