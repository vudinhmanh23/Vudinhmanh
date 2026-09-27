using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Api.Models;

// Represents a sales order placed by a customer
public class Order
{
    [Key]
    public int Id { get; set; }

    [Required]
    public DateTime OrderDate { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    // Line items belonging to this order
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}
