using SalesInventory.Application.Dtos;

namespace SalesInventory.Application.Interfaces;

public interface IDashboardService
{
    // With both dates, also compares revenue with the equally long period right before `from`.
    // `to` without a time part (midnight) covers that whole day; with a time part it is inclusive up to that instant
    Task<DashboardSummaryDto> GetSummaryAsync(DateTime? from, DateTime? to);

    Task<IReadOnlyList<DashboardLowStockItemDto>> GetLowStockItemsAsync(int limit);

    // Revenue of completed orders per day, month or quarter, oldest first. Without dates: the last 12 calendar months
    // (the last 30 days for Day). `to` without a time part covers that whole day. A single missing bound is derived from the other.
    // With compare, every row also carries the same period one year earlier (periods only present last year get Revenue 0)
    Task<IReadOnlyList<RevenueByPeriodDto>> GetRevenueReportAsync(DateTime? from, DateTime? to, RevenueGroupBy groupBy, bool compare);
}
