namespace SalesInventory.Application.Dtos;

// KPI numbers for the dashboard cards; every figure is computed by the database, never supplied by the client
public class DashboardSummaryDto
{
    // Sum of TotalAmount (after discount) of the completed sales orders in the period
    public decimal TotalRevenue { get; set; }

    // Number of completed sales orders in the period
    public int OrderCount { get; set; }

    // TotalRevenue / OrderCount rounded to 2 decimals; 0 when there are no orders (never a division by zero)
    public decimal AverageOrderValue { get; set; }

    // Revenue of the equally long period immediately before this one; null when from or to is missing
    public decimal? PreviousPeriodRevenue { get; set; }

    // (TotalRevenue - PreviousPeriodRevenue) / PreviousPeriodRevenue x 100, rounded to 1 decimal;
    // null when there is no previous period or its revenue is 0 (a percentage of zero is undefined)
    public decimal? RevenueChangePercent { get; set; }

    // Sum of StockQuantity x PurchasePrice over every product (stock valued at cost)
    public decimal InventoryValue { get; set; }

    // Active products whose stock is at or below their own LowStockThreshold
    public int LowStockCount { get; set; }
}

// One row of the "important alerts" list: a product that is running out
public class DashboardLowStockItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
    public int LowStockThreshold { get; set; }
}
