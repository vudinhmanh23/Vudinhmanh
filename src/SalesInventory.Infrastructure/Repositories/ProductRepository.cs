using Microsoft.EntityFrameworkCore;
using SalesInventory.Application.Dtos;
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

    public async Task<(IReadOnlyList<ProductListItemDto> Items, int TotalCount)> QueryAsync(ProductQueryParameters query)
    {
        var products = _context.Products.AsNoTracking();

        // Each filter is added only when its parameter has a value
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            // ToLower on both sides keeps the match case-insensitive on every provider (SQLite's instr() is case-sensitive)
            var term = query.Keyword.Trim().ToLower();
            products = products.Where(p =>
                p.Name.ToLower().Contains(term) ||
                p.Sku.ToLower().Contains(term) ||
                (p.Description != null && p.Description.ToLower().Contains(term)));
        }

        if (query.CategoryId is not null)
        {
            products = products.Where(p => p.CategoryId == query.CategoryId);
        }

        if (query.MinPrice is not null)
        {
            products = products.Where(p => p.SalePrice >= query.MinPrice);
        }

        if (query.MaxPrice is not null)
        {
            products = products.Where(p => p.SalePrice <= query.MaxPrice);
        }

        // Applied only when the flag is true, so "false" means "no stock filter", not "out of stock only"
        if (query.InStockOnly)
        {
            products = products.Where(p => p.StockQuantity > 0);
        }

        var totalCount = await products.CountAsync();

        // Project in SQL so only the list columns are read, not whole entities
        var items = await ApplySort(products, query.SortBy, query.SortDescending)
            .ThenBy(p => p.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new ProductListItemDto
            {
                Id = p.Id,
                Name = p.Name,
                Sku = p.Sku,
                Unit = p.Unit,
                SalePrice = p.SalePrice,
                Quantity = p.StockQuantity,
                IsActive = p.IsActive,
                ImageUrl = p.ImageUrl,
                CategoryId = p.CategoryId,
                CategoryName = p.Category != null ? p.Category.Name : null,
                SupplierName = p.Supplier != null ? p.Supplier.Name : null
            })
            .ToListAsync();

        return (items, totalCount);
    }

    // Upper bound on sort keys so a long SortBy cannot build an absurd ORDER BY
    private const int MaxSortKeys = 4;

    // Builds OrderBy(...).ThenBy(...) from a comma-separated SortBy. Only whitelisted keys are accepted; each one maps
    // to a fixed column expression, so user text never reaches the SQL. Id is added by the caller as the final
    // tie-breaker so pages stay stable when many products share a value.
    private static IOrderedQueryable<Product> ApplySort(IQueryable<Product> products, string? sortBy, bool descending)
    {
        var keys = (sortBy ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => k.ToLowerInvariant())
            .Where(k => k is "name" or "price" or "stock" or "category")
            .Distinct()
            .Take(MaxSortKeys)
            .ToList();

        // Nothing valid (or nothing given): fall back to sorting by name
        if (keys.Count == 0)
        {
            keys.Add("name");
        }

        IOrderedQueryable<Product>? ordered = null;
        foreach (var key in keys)
        {
            ordered = (key, ordered is null, descending) switch
            {
                ("price", true, false) => products.OrderBy(p => p.SalePrice),
                ("price", true, true) => products.OrderByDescending(p => p.SalePrice),
                ("price", false, false) => ordered!.ThenBy(p => p.SalePrice),
                ("price", false, true) => ordered!.ThenByDescending(p => p.SalePrice),

                ("stock", true, false) => products.OrderBy(p => p.StockQuantity),
                ("stock", true, true) => products.OrderByDescending(p => p.StockQuantity),
                ("stock", false, false) => ordered!.ThenBy(p => p.StockQuantity),
                ("stock", false, true) => ordered!.ThenByDescending(p => p.StockQuantity),

                ("category", true, false) => products.OrderBy(p => p.Category!.Name),
                ("category", true, true) => products.OrderByDescending(p => p.Category!.Name),
                ("category", false, false) => ordered!.ThenBy(p => p.Category!.Name),
                ("category", false, true) => ordered!.ThenByDescending(p => p.Category!.Name),

                (_, true, false) => products.OrderBy(p => p.Name),
                (_, true, true) => products.OrderByDescending(p => p.Name),
                (_, false, false) => ordered!.ThenBy(p => p.Name),
                _ => ordered!.ThenByDescending(p => p.Name)
            };
        }

        return ordered!;
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