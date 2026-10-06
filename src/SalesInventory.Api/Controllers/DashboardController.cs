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

    public DashboardController(IProductService productService)
    {
        _productService = productService;
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
