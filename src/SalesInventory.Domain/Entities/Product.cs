using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Domain.Entities;

// Represents a product in stock
public class Product
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Sku { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public int StockQuantity { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    // Optional: a product may have no supplier assigned
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    // Timestamp when this product record was first created, for auditing/reporting
    public DateTime CreatedAt { get; set; }
}
