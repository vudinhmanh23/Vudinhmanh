using SalesInventory.Api.Models;

namespace SalesInventory.Api.Services;

// Business-facing operations for Order (sales), on top of the repository layer
public interface ISalesOrderService
{
    Task<IEnumerable<Order>> GetOrdersAsync();
    Task<Order?> GetOrderAsync(int id);
    Task<IEnumerable<OrderItem>> GetOrderItemsAsync();
    Task<Order> CreateOrderAsync(Order order, IEnumerable<OrderItem> items);
    Task<bool> DeleteOrderAsync(int id);
}
