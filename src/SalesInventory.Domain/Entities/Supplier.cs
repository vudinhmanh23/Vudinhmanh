using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Domain.Entities;

// Represents a product supplier
public class Supplier
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    // A supplier can have many products
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
