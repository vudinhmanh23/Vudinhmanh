using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Interfaces;

public interface ICustomerRepository : IRepository<Customer>
{
    // One page of customers ordered by Id, plus the total number of customers
    Task<(IReadOnlyList<Customer> Items, int TotalCount)> GetPagedAsync(int page, int pageSize);

    Task<bool> HasOrdersAsync(int customerId);
}
