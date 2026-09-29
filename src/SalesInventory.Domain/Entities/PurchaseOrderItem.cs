using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Domain.Entities;

// Junction entity linking PurchaseOrder and Product, forming a many-to-many relationship via purchase line items
public class PurchaseOrderItem
{
    [Key]
    public int Id { get; set; }

    public int PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}
