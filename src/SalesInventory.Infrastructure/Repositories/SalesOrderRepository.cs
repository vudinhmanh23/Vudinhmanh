using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SalesInventory.Application.Exceptions;
using SalesInventory.Application.Interfaces;
using SalesInventory.Domain.Entities;
using SalesInventory.Infrastructure.Persistence;

namespace SalesInventory.Infrastructure.Repositories;

public class SalesOrderRepository : Repository<SalesOrder>, ISalesOrderRepository
{
    public SalesOrderRepository(AppDbContext context) : base(context)
    {
    }

    // Race losers are reported as one retryable exception instead of provider-specific ones:
    // stale Product.RowVersion, duplicate order number (unique index) and SQL Server deadlock victims
    public override async Task SaveChangesAsync()
    {
        try
        {
            await base.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("The data was modified by another request.", ex);
        }
        // 2601/2627: duplicate key (another request took the same order number); 1205: deadlock victim
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 or 1205 })
        {
            throw new ConcurrencyConflictException("Another request is changing the same data.", ex);
        }
    }

    // Read-only projection: the order, its customer's name and each line's product name. Whole Product/Customer rows
    // (description, image URL, address, ...) are never read; nothing here is tracked. The shape is still SalesOrder,
    // so mapping to the DTOs and the invoice are unchanged.
    private IQueryable<SalesOrder> OrdersWithItems(IQueryable<SalesOrder> orders)
    {
        return orders.Select(o => new SalesOrder
        {
            Id = o.Id,
            OrderNumber = o.OrderNumber,
            OrderDate = o.OrderDate,
            CustomerId = o.CustomerId,
            Customer = new Customer { Id = o.Customer!.Id, Name = o.Customer.Name },
            DiscountAmount = o.DiscountAmount,
            TotalAmount = o.TotalAmount,
            Note = o.Note,
            Status = o.Status,
            Items = o.Items.Select(i => new SalesOrderItem
            {
                Id = i.Id,
                SalesOrderId = i.SalesOrderId,
                ProductId = i.ProductId,
                Product = new Product { Id = i.Product!.Id, Name = i.Product.Name },
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                LineTotal = i.LineTotal
            }).ToList()
        });
    }

    public async Task<IReadOnlyList<SalesOrder>> GetAllWithItemsAsync(int? page = null, int? pageSize = null)
    {
        // Newest first; Id breaks ties. Without page/pageSize everything is returned, as before
        var ordered = _context.SalesOrders
            .AsNoTracking()
            .OrderByDescending(o => o.OrderDate)
            .ThenByDescending(o => o.Id);

        var paged = page is { } p && pageSize is { } size
            ? ordered.Skip((p - 1) * size).Take(size)
            : ordered;

        return await OrdersWithItems(paged).ToListAsync();
    }

    public async Task<IReadOnlyList<SalesOrder>> GetByCustomerWithItemsAsync(int customerId)
    {
        return await OrdersWithItems(_context.SalesOrders
                .AsNoTracking()
                .Where(o => o.CustomerId == customerId)
                .OrderByDescending(o => o.OrderDate)
                .ThenByDescending(o => o.Id))
            .ToListAsync();
    }

    public async Task<SalesOrder?> GetWithItemsAsync(int id)
    {
        return await OrdersWithItems(_context.SalesOrders.AsNoTracking().Where(o => o.Id == id))
            .FirstOrDefaultAsync();
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
