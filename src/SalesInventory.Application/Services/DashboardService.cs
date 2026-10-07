using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;

namespace SalesInventory.Application.Services;

public class DashboardService : IDashboardService
{
    private const int MaxLowStockItems = 50;

    private readonly IDashboardRepository _repository;

    public DashboardService(IDashboardRepository repository)
    {
        _repository = repository;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(DateTime? from, DateTime? to)
    {
        var toExclusive = ToExclusiveUpperBound(to);
        var summary = await _repository.GetSummaryAsync(from, toExclusive);

        // Derived from the two server-side numbers; no orders means an average of 0, not a division by zero
        summary.AverageOrderValue = summary.OrderCount == 0
            ? 0m
            : Math.Round(summary.TotalRevenue / summary.OrderCount, 2, MidpointRounding.AwayFromZero);

        // The previous period only exists when both ends are known
        if (from is { } start && toExclusive is { } end)
        {
            // Same length, ending exactly where this period starts: [start - length, start).
            // The bounds are half-open, so no instant belongs to both periods and none is skipped.
            var length = end - start;
            if (length > TimeSpan.Zero && start.Ticks - length.Ticks >= DateTime.MinValue.Ticks)
            {
                var previousRevenue = await _repository.GetRevenueAsync(start - length, start);
                summary.PreviousPeriodRevenue = previousRevenue;
                summary.RevenueChangePercent = previousRevenue == 0m
                    ? null
                    : Math.Round((summary.TotalRevenue - previousRevenue) / previousRevenue * 100m, 1, MidpointRounding.AwayFromZero);
            }
        }

        return summary;
    }

    public async Task<IReadOnlyList<DashboardLowStockItemDto>> GetLowStockItemsAsync(int limit)
    {
        return await _repository.GetLowStockItemsAsync(Math.Clamp(limit, 1, MaxLowStockItems));
    }

    // A date picked as "to" means the end of that day, so a date-only value becomes the start of the next day (exclusive)
    private static DateTime? ToExclusiveUpperBound(DateTime? to)
    {
        if (to is not { } value)
        {
            return null;
        }

        // Too close to DateTime.MaxValue to add anything: effectively no upper bound
        if (value.Date >= DateTime.MaxValue.Date)
        {
            return null;
        }

        return value.TimeOfDay == TimeSpan.Zero ? value.AddDays(1) : value.AddTicks(1);
    }
}
