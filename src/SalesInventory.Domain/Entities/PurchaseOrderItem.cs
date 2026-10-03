using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Domain.Entities;

// Line item of a PurchaseOrder: one product with its quantity and unit price (1-N from PurchaseOrder)
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

    // Quantity * UnitPrice; always computed on the server
    public decimal LineTotal { get; set; }
}
