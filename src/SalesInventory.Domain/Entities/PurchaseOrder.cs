using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Domain.Entities;

// Represents a purchase order placed with a supplier to restock inventory
public class PurchaseOrder
{
    [Key]
    public int Id { get; set; }

    [Required]
    public DateTime OrderDate { get; set; }

    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    // Line items belonging to this purchase order
    public ICollection<PurchaseOrderItem> PurchaseOrderItems { get; set; } = new List<PurchaseOrderItem>();
}
