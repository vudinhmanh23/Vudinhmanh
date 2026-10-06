using Microsoft.EntityFrameworkCore;
using SalesInventory.Application.Interfaces;
using SalesInventory.Domain.Entities;
using SalesInventory.Infrastructure.Persistence;

namespace SalesInventory.Infrastructure.Repositories;

public class ProductRepository : Repository<Product>, IProductRepository
{
    public ProductRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<Product>> GetBelowReorderLevelAsync(bool includeInactive = false)
    {
        // One SQL statement: WHERE ReorderLevel > 0 AND StockQuantity <= ReorderLevel [AND IsActive = 1]
        // ORDER BY (ReorderLevel - StockQuantity) DESC, Id
        return await _context.Products
            .AsNoTracking()
            .Where(p => (includeInactive || p.IsActive) && p.ReorderLevel > 0 && p.StockQuantity <= p.ReorderLevel)
            .OrderByDescending(p => p.ReorderLevel - p.StockQuantity)
            .ThenBy(p => p.Id)
            .ToListAsync();
    }

    public async Task<InventorySummary> GetInventorySummaryAsync()
    {
        // Same low-stock rule as the low-stock endpoint's default (active products only). The inventory value
        // covers every product: stock of a discontinued product is still on the shelf and still has a cost.
        // GroupBy on a constant folds the four aggregates into one SELECT.
        var summary = await _context.Products
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Active = g.Count(p => p.IsActive),
                Low = g.Count(p => p.IsActive && p.ReorderLevel > 0 && p.StockQuantity <= p.ReorderLevel),
                Value = g.Sum(p => (decimal)p.StockQuantity * p.PurchasePrice)
            })
            .FirstOrDefaultAsync();

        // No products at all: the GroupBy yields no row
        return summary is null
            ? new InventorySummary(0, 0, 0, 0m)
            : new InventorySummary(summary.Total, summary.Active, summary.Low, summary.Value);
    }
}
