using Microsoft.EntityFrameworkCore;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;
using SalesInventory.Domain.Enums;
using SalesInventory.Infrastructure.Persistence;

namespace SalesInventory.Infrastructure.Repositories;

public class DashboardRepository : IDashboardRepository
{
    private readonly AppDbContext _context;

    public DashboardRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(DateTime? from, DateTime? toExclusive)
    {
        // Cancelled orders are not sales, so they count neither as revenue nor as orders
        var orders = _context.SalesOrders.AsNoTracking().Where(o => o.Status == SalesOrderStatus.Completed);

        if (from is not null)
        {
            orders = orders.Where(o => o.OrderDate >= from);
        }

        if (toExclusive is not null)
        {
            orders = orders.Where(o => o.OrderDate < toExclusive);
        }

        // GroupBy on a constant folds SUM and COUNT into one SELECT; no order rows are loaded
        var sales = await orders
            .GroupBy(_ => 1)
            .Select(g => new { Revenue = g.Sum(o => o.TotalAmount), Count = g.Count() })
            .FirstOrDefaultAsync();

        // Same valuation and low-stock rule as the stock screens: value at cost, active products only for alerts
        var stock = await _context.Products
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Value = g.Sum(p => (decimal)p.StockQuantity * p.PurchasePrice),
                Low = g.Count(p => p.IsActive && p.StockQuantity <= p.LowStockThreshold)
            })
            .FirstOrDefaultAsync();

        // An empty table yields no group row, hence the null fallbacks
        return new DashboardSummaryDto
        {
            TotalRevenue = sales?.Revenue ?? 0m,
            OrderCount = sales?.Count ?? 0,
            InventoryValue = stock?.Value ?? 0m,
            LowStockCount = stock?.Low ?? 0
        };
    }

    public async Task<decimal> GetRevenueAsync(DateTime from, DateTime toExclusive)
    {
        // Same filter as the summary (completed only, lower bound inclusive, upper bound exclusive)
        return await _context.SalesOrders
            .AsNoTracking()
            .Where(o => o.Status == SalesOrderStatus.Completed && o.OrderDate >= from && o.OrderDate < toExclusive)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;
    }

    public async Task<IReadOnlyList<DashboardLowStockItemDto>> GetLowStockItemsAsync(int limit)
    {
        // Same predicate as LowStockCount, so the list and the card always agree
        return await _context.Products
            .AsNoTracking()
            .Where(p => p.IsActive && p.StockQuantity <= p.LowStockThreshold)
            .OrderBy(p => p.StockQuantity)
            .ThenBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Take(limit)
            .Select(p => new DashboardLowStockItemDto
            {
                Id = p.Id,
                Name = p.Name,
                StockQuantity = p.StockQuantity,
                LowStockThreshold = p.LowStockThreshold
            })
            .ToListAsync();
    }
}
