using SalesInventory.Application.Dtos;

namespace SalesInventory.Application.Interfaces;

// Read-only aggregate queries for the dashboard; all summing and counting happens in the database
public interface IDashboardRepository
{
    // Completed orders with OrderDate in [from, toExclusive); a null bound means "no limit on that side"
    Task<DashboardSummaryDto> GetSummaryAsync(DateTime? from, DateTime? toExclusive);

    // Revenue of completed orders with OrderDate in [from, toExclusive), summed by the database
    Task<decimal> GetRevenueAsync(DateTime from, DateTime toExclusive);

    // Completed orders with OrderDate in [from, toExclusive) grouped by day, month or quarter (GROUP BY in SQL), oldest first
    Task<IReadOnlyList<RevenuePoint>> GetRevenueByPeriodAsync(DateTime from, DateTime toExclusive, RevenueGroupBy groupBy);

    // Active products at or below their LowStockThreshold, lowest stock first, at most `limit` rows
    Task<IReadOnlyList<DashboardLowStockItemDto>> GetLowStockItemsAsync(int limit);
}
