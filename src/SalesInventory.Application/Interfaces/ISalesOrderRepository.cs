using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Interfaces;

public interface ISalesOrderRepository : IRepository<SalesOrder>
{
    // Orders with their items (and each item's product), newest first
    // Optional page/pageSize (both 1-based/positive) limit the result; without them every order is returned.
    // Read-only: only the columns the DTOs need are read (customer and product names), nothing is tracked
    Task<IReadOnlyList<SalesOrder>> GetAllWithItemsAsync(int? page = null, int? pageSize = null);

    Task<SalesOrder?> GetWithItemsAsync(int id);

    // One order by its human-readable number (e.g. SO-20261006-0001); read-only, only the columns an order summary needs
    Task<SalesOrder?> GetByOrderNumberAsync(string orderNumber);

    // One customer's orders with items, newest first
    Task<IReadOnlyList<SalesOrder>> GetByCustomerWithItemsAsync(int customerId);

    // Highest existing order number starting with the prefix, or null when none
    Task<string?> GetLastOrderNumberWithPrefixAsync(string prefix);
}
