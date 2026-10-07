using System.Globalization;
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

    public async Task<IReadOnlyList<RevenueByPeriodDto>> GetRevenueReportAsync(DateTime? from, DateTime? to, RevenueGroupBy groupBy, bool compare)
    {
        // Default window: 12 calendar months ending today, or the last 30 days when grouping by day
        var end = (to ?? (from is { } f ? f.AddMonths(11) : DateTime.Today)).Date;
        var start = from ?? (groupBy == RevenueGroupBy.Day
            ? end.AddDays(-29)
            : new DateTime(end.Year, end.Month, 1).AddMonths(-11));

        // The whole of the last day is included
        var toExclusive = ToExclusiveUpperBound(end) ?? DateTime.MaxValue;
        var current = await _repository.GetRevenueByPeriodAsync(start, toExclusive, groupBy);

        if (!compare || start.Year <= 1)
        {
            return current.Select(p => ToDto(p, groupBy, null)).ToList();
        }

        // Same window shifted back exactly one calendar year (29 Feb becomes 28 Feb)
        var previous = await _repository.GetRevenueByPeriodAsync(start.AddYears(-1), toExclusive.AddYears(-1), groupBy);

        // Key last year's buckets by the period they line up with this year; 29 Feb has no counterpart, so it is skipped
        var previousByPeriod = previous
            .Where(p => !(p.PeriodStart.Month == 2 && p.PeriodStart.Day == 29))
            .ToDictionary(p => p.PeriodStart.AddYears(1));
        var currentByPeriod = current.ToDictionary(p => p.PeriodStart);

        // Union of both sides, so a period that only had sales last year still shows up (with revenue 0 this year)
        return currentByPeriod.Keys.Union(previousByPeriod.Keys)
            .OrderBy(k => k)
            .Select(k => ToDto(
                currentByPeriod.GetValueOrDefault(k, new RevenuePoint(k, 0m, 0)),
                groupBy,
                previousByPeriod.GetValueOrDefault(k, new RevenuePoint(k, 0m, 0))))
            .ToList();
    }

    private static RevenueByPeriodDto ToDto(RevenuePoint point, RevenueGroupBy groupBy, RevenuePoint? previousYear)
    {
        return new RevenueByPeriodDto
        {
            Period = FormatPeriod(point.PeriodStart, groupBy),
            Revenue = point.Revenue,
            OrderCount = point.OrderCount,
            PreviousYearRevenue = previousYear?.Revenue,
            PreviousYearOrderCount = previousYear?.OrderCount
        };
    }

    private static string FormatPeriod(DateTime start, RevenueGroupBy groupBy) => groupBy switch
    {
        RevenueGroupBy.Day => start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        RevenueGroupBy.Quarter => $"{start.Year:D4}-Q{(start.Month - 1) / 3 + 1}",
        _ => start.ToString("yyyy-MM", CultureInfo.InvariantCulture)
    };

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
