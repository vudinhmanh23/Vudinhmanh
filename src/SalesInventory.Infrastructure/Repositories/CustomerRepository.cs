using Microsoft.EntityFrameworkCore;
using SalesInventory.Application.Interfaces;
using SalesInventory.Domain.Entities;
using SalesInventory.Infrastructure.Persistence;

namespace SalesInventory.Infrastructure.Repositories;

public class CustomerRepository : Repository<Customer>, ICustomerRepository
{
    public CustomerRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<(IReadOnlyList<Customer> Items, int TotalCount)> GetPagedAsync(int page, int pageSize)
    {
        var query = _context.Customers.AsNoTracking();
        var total = await query.CountAsync();
        var items = await query
            .OrderBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public Task<bool> HasOrdersAsync(int customerId)
    {
        return _context.Orders.AnyAsync(o => o.CustomerId == customerId);
    }
}
