namespace SalesInventory.Application.Dtos;

// One row of the revenue report (a day, month or quarter depending on groupBy)
public class RevenueByPeriodDto
{
    // Day: yyyy-MM-dd, month: yyyy-MM, quarter: yyyy-Qn
    public string Period { get; set; } = string.Empty;

    // Sum of TotalAmount of the completed orders in that period
    public decimal Revenue { get; set; }

    public int OrderCount { get; set; }

    // Same period one year earlier; both stay null unless the year-over-year comparison was requested
    public decimal? PreviousYearRevenue { get; set; }

    public int? PreviousYearOrderCount { get; set; }
}
