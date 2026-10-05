using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Domain.Entities;

// Represents a sales order placed by a customer
public class SalesOrder
{
    [Key]
    public int Id { get; set; }

    [Required]
    public DateTime OrderDate { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    // Flat discount subtracted from the sum of the line totals
    public decimal DiscountAmount { get; set; }

    // Sum of LineTotal minus DiscountAmount, never negative; always computed server-side
    public decimal TotalAmount { get; set; }

    // Line items belonging to this order
    public ICollection<SalesOrderItem> Items { get; set; } = new List<SalesOrderItem>();
}
