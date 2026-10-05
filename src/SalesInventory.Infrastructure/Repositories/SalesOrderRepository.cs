using Microsoft.EntityFrameworkCore;
using SalesInventory.Application.Interfaces;
using SalesInventory.Domain.Entities;
using SalesInventory.Infrastructure.Persistence;

namespace SalesInventory.Infrastructure.Repositories;

public class SalesOrderRepository : Repository<SalesOrder>, ISalesOrderRepository
{
    public SalesOrderRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<SalesOrder>> GetAllWithItemsAsync()
    {
        return await _context.SalesOrders
            .AsNoTracking()
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .OrderByDescending(o => o.OrderDate)
            .ThenByDescending(o => o.Id)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<SalesOrder>> GetByCustomerWithItemsAsync(int customerId)
    {
        return await _context.SalesOrders
            .AsNoTracking()
            .Where(o => o.CustomerId == customerId)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .OrderByDescending(o => o.OrderDate)
            .ThenByDescending(o => o.Id)
            .ToListAsync();
    }

    public async Task<SalesOrder?> GetWithItemsAsync(int id)
    {
        return await _context.SalesOrders
            .AsNoTracking()
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<string?> GetLastOrderNumberWithPrefixAsync(string prefix)
    {
        // Longest first so a 5-digit sequence sorts after a 4-digit one
        return await _context.SalesOrders
            .Where(o => o.OrderNumber.StartsWith(prefix))
            .OrderByDescending(o => o.OrderNumber.Length)
            .ThenByDescending(o => o.OrderNumber)
            .Select(o => o.OrderNumber)
            .FirstOrDefaultAsync();
    }
}
