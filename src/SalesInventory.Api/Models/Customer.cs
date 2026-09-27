using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Api.Models;

// Represents a customer who places orders
public class Customer
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    // A customer can place many orders
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
