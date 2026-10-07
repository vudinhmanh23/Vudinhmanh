namespace SalesInventory.Application.Dtos;

// Bucket size of the revenue report
public enum RevenueGroupBy
{
    Day,
    Month,
    Quarter
}

// One aggregated bucket; PeriodStart is the first day of the day/month/quarter
public record RevenuePoint(DateTime PeriodStart, decimal Revenue, int OrderCount);
