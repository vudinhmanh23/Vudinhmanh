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

    [MaxLength(1000)]
    public string? Description { get; set; }

    // Optional barcode; when present it must be unique (filtered unique index)
    [MaxLength(50)]
    public string? Barcode { get; set; }

    // Unit of measure, e.g. "cái", "hộp", "kg"
    [Required]
    [MaxLength(50)]
    public string Unit { get; set; } = "cái";

    public decimal Price { get; set; }

    public decimal PurchasePrice { get; set; }

    public decimal SalePrice { get; set; }

    public bool IsActive { get; set; } = true;

    public int StockQuantity { get; set; }

    // A sale that leaves StockQuantity <= this value raises a low-stock warning (it never blocks the sale)
    public int LowStockThreshold { get; set; } = 5;

    // Minimum stock the business wants to keep on hand; informational only, no stock logic reads it yet
    public int ReorderLevel { get; set; }

    // Optimistic concurrency token: two sales racing for the same stock cannot both win
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    // Optional: a product may have no supplier assigned
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    // Timestamp when this product record was first created, for auditing/reporting
    public DateTime CreatedAt { get; set; }
}
