using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Interfaces;

public interface ISalesOrderRepository : IRepository<SalesOrder>
{
    // Orders with their items (and each item's product), newest first
    Task<IReadOnlyList<SalesOrder>> GetAllWithItemsAsync();

    Task<SalesOrder?> GetWithItemsAsync(int id);

    // One customer's orders with items, newest first
    Task<IReadOnlyList<SalesOrder>> GetByCustomerWithItemsAsync(int customerId);
}
