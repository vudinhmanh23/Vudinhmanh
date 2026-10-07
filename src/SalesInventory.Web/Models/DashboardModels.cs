namespace SalesInventory.Web.Models;

/// <summary>Mirror of the API's DashboardSummaryDto (all figures are computed server-side).</summary>
public record DashboardSummary(
    decimal TotalRevenue,
    int OrderCount,
    decimal AverageOrderValue,
    decimal? PreviousPeriodRevenue,
    decimal? RevenueChangePercent,
    decimal InventoryValue,
    int LowStockCount);

/// <summary>One product in the "important alerts" list.</summary>
public record LowStockAlert(int Id, string Name, int StockQuantity, int LowStockThreshold);

/// <summary>One bucket of GET /api/reports/revenue; Period is yyyy-MM-dd, yyyy-MM or yyyy-Qn. PreviousYear* are set only when compare=true.</summary>
public record RevenueByPeriod(string Period, decimal Revenue, int OrderCount, decimal? PreviousYearRevenue = null, int? PreviousYearOrderCount = null);
