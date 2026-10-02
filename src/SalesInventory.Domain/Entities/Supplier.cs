using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Domain.Entities;

// Represents a product supplier
public class Supplier
{
    [Key]
    public int Id { get; set; }

    // Business code, must be unique (e.g. "SUP-001")
    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? ContactPerson { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    // Soft flag: keep history instead of hard delete
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // A supplier can have many products
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
