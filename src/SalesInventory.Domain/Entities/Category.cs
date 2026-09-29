using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Domain.Entities;

// Represents a product category
public class Category
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    // A category can have many products
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
