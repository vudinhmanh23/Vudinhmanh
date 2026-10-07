using SalesInventory.Application.Dtos;
using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Interfaces;

// The persisted order plus any non-blocking low-stock warnings
public record CreateSalesOrderResult(
    SalesOrder Order,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<LowStockProductDto> LowStockProducts);

// Business-facing operations for SalesOrder, on top of the repository layer
public interface ISalesOrderService
{
    // Optional page/pageSize limit the list (newest first); without them every order is returned
    Task<IReadOnlyList<SalesOrder>> GetOrdersAsync(int? page = null, int? pageSize = null);
    Task<int> CountOrdersAsync();
    Task<SalesOrder?> GetOrderAsync(int id);

    // Sales history of one customer, newest first (empty when the customer has no orders)
    Task<IReadOnlyList<SalesOrder>> GetOrdersByCustomerAsync(int customerId);

    // Validates, deducts stock, logs one Sale movement per line and saves everything in one transaction
    Task<CreateSalesOrderResult> CreateOrderAsync(SalesOrder order);
    Task<bool> DeleteOrderAsync(int id);
}
