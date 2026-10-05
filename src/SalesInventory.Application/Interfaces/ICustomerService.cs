using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Interfaces;

// Business-facing operations for Customer
public interface ICustomerService
{
    Task<(IReadOnlyList<Customer> Items, int TotalCount)> GetCustomersAsync(int page, int pageSize);
    Task<Customer?> GetCustomerAsync(int id);
    Task<Customer> CreateCustomerAsync(Customer customer);
    Task<bool> UpdateCustomerAsync(int id, Customer customer);
    Task<bool> DeleteCustomerAsync(int id);
}
