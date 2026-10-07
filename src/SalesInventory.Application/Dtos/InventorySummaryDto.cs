namespace SalesInventory.Application.Dtos;

// Dashboard numbers for the whole catalogue
public class InventorySummaryDto
{
    // Every product, active or not
    public int TotalProducts { get; set; }

    public int ActiveProducts { get; set; }

    // Active products with ReorderLevel > 0 and StockQuantity <= ReorderLevel (same rule as GET /api/products/low-stock)
    public int LowStockProducts { get; set; }

    // SUM(StockQuantity * PurchasePrice) over every product, as decimal (no floating-point error)
    public decimal InventoryValue { get; set; }
}
