using Microsoft.EntityFrameworkCore;
using SalesInventory.Application.Interfaces;
using SalesInventory.Domain.Entities;
using SalesInventory.Infrastructure.Persistence;

namespace SalesInventory.Infrastructure.Repositories;

public class PurchaseOrderRepository : Repository<PurchaseOrder>, IPurchaseOrderRepository
{
    public PurchaseOrderRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<PurchaseOrder?> GetWithItemsAsync(int id)
    {
        return await _context.PurchaseOrders
            .Include(po => po.PurchaseOrderItems)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(po => po.Id == id);
    }

    public async Task<string?> GetLastCodeWithPrefixAsync(string prefix)
    {
        // Longest first so a 4-digit sequence (…-1000) sorts after a 3-digit one (…-999)
        return await _context.PurchaseOrders
            .Where(po => po.Code.StartsWith(prefix))
            .OrderByDescending(po => po.Code.Length)
            .ThenByDescending(po => po.Code)
            .Select(po => po.Code)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<PurchaseOrder>> GetAllWithItemsAsync()
    {
        return await _context.PurchaseOrders
            .Include(po => po.PurchaseOrderItems)
                .ThenInclude(i => i.Product)
            .OrderByDescending(po => po.OrderDate)
            .ThenByDescending(po => po.Id)
            .ToListAsync();
    }
}
