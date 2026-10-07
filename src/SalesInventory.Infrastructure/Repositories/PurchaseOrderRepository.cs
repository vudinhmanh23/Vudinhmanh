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

    // Tracked, with whole entities: for code that changes the order (approve, cancel, update)
    public async Task<PurchaseOrder?> GetWithItemsAsync(int id)
    {
        return await _context.PurchaseOrders
            .Include(po => po.PurchaseOrderItems)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(po => po.Id == id);
    }

    // Read-only projection: only the order columns and each line's product name; nothing is tracked
    private static IQueryable<PurchaseOrder> OrdersForRead(IQueryable<PurchaseOrder> orders)
    {
        return orders.Select(po => new PurchaseOrder
        {
            Id = po.Id,
            Code = po.Code,
            Status = po.Status,
            OrderDate = po.OrderDate,
            SupplierId = po.SupplierId,
            TotalAmount = po.TotalAmount,
            Note = po.Note,
            PurchaseOrderItems = po.PurchaseOrderItems.Select(i => new PurchaseOrderItem
            {
                Id = i.Id,
                PurchaseOrderId = i.PurchaseOrderId,
                ProductId = i.ProductId,
                Product = new Product { Id = i.Product!.Id, Name = i.Product.Name },
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                LineTotal = i.LineTotal
            }).ToList()
        });
    }

    public async Task<PurchaseOrder?> GetForReadAsync(int id)
    {
        return await OrdersForRead(_context.PurchaseOrders.AsNoTracking().Where(po => po.Id == id))
            .FirstOrDefaultAsync();
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

    public async Task<IReadOnlyList<PurchaseOrder>> GetAllWithItemsAsync(int? page = null, int? pageSize = null)
    {
        // Newest first; Id breaks ties. Without page/pageSize everything is returned, as before
        var ordered = _context.PurchaseOrders
            .AsNoTracking()
            .OrderByDescending(po => po.OrderDate)
            .ThenByDescending(po => po.Id);

        var paged = page is { } p && pageSize is { } size
            ? ordered.Skip((p - 1) * size).Take(size)
            : ordered;

        return await OrdersForRead(paged).ToListAsync();
    }
}
