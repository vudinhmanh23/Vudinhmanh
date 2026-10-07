using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;
using SalesInventory.Infrastructure.Identity;

namespace SalesInventory.Api.Controllers;

// Read-only aggregates for dashboards; stock value is sensitive, so it follows the inventory-management policy
[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = AuthPolicies.CanManageInventory)]
public class DashboardController : ControllerBase
{
    private readonly IProductService _productService;
    private readonly IDashboardService _dashboardService;

    public DashboardController(IProductService productService, IDashboardService dashboardService)
    {
        _productService = productService;
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// KPI cards: revenue and order count of completed sales in [from, to], inventory value (stock x cost price)
    /// and the number of active products at or below their low-stock threshold. Money is summed by the database.
    /// Both dates are optional (no bound when omitted); a <c>to</c> without a time covers that whole day.
    /// With both dates, previousPeriodRevenue / revenueChangePercent compare against the equally long period right before <c>from</c>.
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        if (from is not null && to is not null && from > to)
        {
            ModelState.AddModelError(nameof(from), "from must not be later than to.");
            return ValidationProblem(ModelState);
        }

        return Ok(await _dashboardService.GetSummaryAsync(from, to));
    }

    /// <summary>
    /// "Important alerts": active products at or below their low-stock threshold, lowest stock first
    /// (the same rule as lowStockCount in the summary). limit defaults to 10, max 50.
    /// </summary>
    [HttpGet("low-stock-items")]
    public async Task<ActionResult<IReadOnlyList<DashboardLowStockItemDto>>> GetLowStockItems([FromQuery] int limit = 10)
    {
        return Ok(await _dashboardService.GetLowStockItemsAsync(limit));
    }

    /// <summary>
    /// Total products, products below their reorder level, and inventory value
    /// (SUM of StockQuantity x PurchasePrice, decimal), all computed by the database.
    /// </summary>
    [HttpGet("inventory-summary")]
    public async Task<ActionResult<InventorySummaryDto>> GetInventorySummary()
    {
        var summary = await _productService.GetInventorySummaryAsync();

        return Ok(new InventorySummaryDto
        {
            TotalProducts = summary.TotalProducts,
            ActiveProducts = summary.ActiveProducts,
            LowStockProducts = summary.LowStockProducts,
            InventoryValue = summary.InventoryValue
        });
    }
}
