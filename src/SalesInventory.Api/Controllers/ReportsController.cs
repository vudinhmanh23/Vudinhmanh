using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;
using SalesInventory.Infrastructure.Identity;

namespace SalesInventory.Api.Controllers;

// Read-only reports; revenue is sensitive, so it follows the same policy as the dashboard
[ApiController]
[Route("api/reports")]
[Authorize(Policy = AuthPolicies.CanManageInventory)]
public class ReportsController : ControllerBase
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IDashboardService _dashboardService;
    private readonly IRevenueExcelService _revenueExcelService;

    public ReportsController(IDashboardService dashboardService, IRevenueExcelService revenueExcelService)
    {
        _dashboardService = dashboardService;
        _revenueExcelService = revenueExcelService;
    }

    /// <summary>
    /// Daily revenue as an Excel file (revenue-report.xlsx, sheet "DoanhThu") with a total row.
    /// Same data as GET /api/reports/revenue?groupBy=day; without dates the last 30 days are exported.
    /// </summary>
    [HttpGet("revenue/excel")]
    [Produces(XlsxContentType)]
    public async Task<IActionResult> GetRevenueExcel([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        if (from is not null && to is not null && from > to)
        {
            ModelState.AddModelError(nameof(from), "from must not be later than to.");
            return ValidationProblem(ModelState);
        }

        var days = await _dashboardService.GetRevenueReportAsync(from, to, RevenueGroupBy.Day, compare: false);
        return File(_revenueExcelService.Generate(days), XlsxContentType, "revenue-report.xlsx");
    }

    /// <summary>
    /// Revenue and order count of completed sales per bucket, oldest first. groupBy = day | month (default) | quarter;
    /// period is yyyy-MM-dd, yyyy-MM or yyyy-Qn. Both dates are optional (YYYY-MM-DD); without them the last 12 months
    /// (30 days for day) are returned. <c>to</c> covers the whole day. Buckets without orders are not listed.
    /// With compare=true each row also has previousYearRevenue / previousYearOrderCount for the same period a year earlier.
    /// </summary>
    [HttpGet("revenue")]
    public async Task<ActionResult<IReadOnlyList<RevenueByPeriodDto>>> GetRevenue(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] RevenueGroupBy groupBy = RevenueGroupBy.Month,
        [FromQuery] bool compare = false)
    {
        // Model binding accepts any number for an enum, so reject values that are not a real bucket
        if (!Enum.IsDefined(groupBy))
        {
            ModelState.AddModelError(nameof(groupBy), "groupBy must be day, month or quarter.");
            return ValidationProblem(ModelState);
        }

        if (from is not null && to is not null && from > to)
        {
            ModelState.AddModelError(nameof(from), "from must not be later than to.");
            return ValidationProblem(ModelState);
        }

        return Ok(await _dashboardService.GetRevenueReportAsync(from, to, groupBy, compare));
    }
}
