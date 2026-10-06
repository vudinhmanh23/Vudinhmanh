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

    public async Task<(IReadOnlyList<Product> Items, int TotalCount)> SearchAsync(
        string? search, int? categoryId, string sortBy, bool descending, int page, int pageSize)
    {
        var query = _context.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => p.Name.Contains(term));
        }

        if (categoryId is not null)
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        var totalCount = await query.CountAsync();

        // Id is the tie-breaker so pages stay stable when many products share a name or price
        var ordered = (sortBy, descending) switch
        {
            ("price", true) => query.OrderByDescending(p => p.SalePrice),
            ("price", false) => query.OrderBy(p => p.SalePrice),
            (_, true) => query.OrderByDescending(p => p.Name),
            _ => query.OrderBy(p => p.Name)
        };

        var items = await ordered
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }
}