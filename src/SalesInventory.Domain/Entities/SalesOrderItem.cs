using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Domain.Entities;

// One line of a sales order: a quantity of a product sold at a unit price
public class SalesOrderItem
{
    [Key]
    public int Id { get; set; }

    public int SalesOrderId { get; set; }
    public SalesOrder? SalesOrder { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    // Quantity * UnitPrice, computed server-side
    public decimal LineTotal { get; set; }
}
