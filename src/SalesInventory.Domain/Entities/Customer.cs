using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Domain.Entities;

// Represents a customer who places orders
public class Customer
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(400)]
    public string? Address { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // A customer can place many orders
    public ICollection<SalesOrder> SalesOrders { get; set; } = new List<SalesOrder>();
}
