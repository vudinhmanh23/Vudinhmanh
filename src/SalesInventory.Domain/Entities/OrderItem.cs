using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Domain.Entities;

// Junction entity linking Order and Product, forming a many-to-many relationship via order line items
public class OrderItem
{
    [Key]
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}
