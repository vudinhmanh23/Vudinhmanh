using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Dtos;

// One row of the low-stock report: a product that has fallen to or below its reorder level
public class LowStockItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
    public int ReorderLevel { get; set; }

    // How many units to buy to get back to the reorder level (never below 0)
    public int Shortage { get; set; }

    // False for discontinued products, which only show up with includeInactive=true
    public bool IsActive { get; set; }

    public static LowStockItemDto From(Product product) => new()
    {
        Id = product.Id,
        Name = product.Name,
        Sku = product.Sku,
        StockQuantity = product.StockQuantity,
        ReorderLevel = product.ReorderLevel,
        Shortage = Math.Max(0, product.ReorderLevel - product.StockQuantity),
        IsActive = product.IsActive
    };
}
