using SalesInventory.Application.Exceptions;
using SalesInventory.Application.Interfaces;
using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Services;

public class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _customerRepository;

    public CustomerService(ICustomerRepository customerRepository)
    {
        _customerRepository = customerRepository;
    }

    public Task<(IReadOnlyList<Customer> Items, int TotalCount)> GetCustomersAsync(int page, int pageSize)
    {
        return _customerRepository.GetPagedAsync(page, pageSize);
    }

    public Task<Customer?> GetCustomerAsync(int id)
    {
        return _customerRepository.GetByIdAsync(id);
    }

    public async Task<Customer> CreateCustomerAsync(Customer customer)
    {
        customer.CreatedAt = DateTime.UtcNow;

        await _customerRepository.AddAsync(customer);
        await _customerRepository.SaveChangesAsync();

        return customer;
    }

    public async Task<bool> UpdateCustomerAsync(int id, Customer customer)
    {
        var existing = await _customerRepository.GetByIdAsync(id);
        if (existing is null)
        {
            return false;
        }

        existing.Name = customer.Name;
        existing.Phone = customer.Phone;
        existing.Email = customer.Email;
        existing.Address = customer.Address;

        _customerRepository.Update(existing);
        await _customerRepository.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteCustomerAsync(int id)
    {
        var existing = await _customerRepository.GetByIdAsync(id);
        if (existing is null)
        {
            return false;
        }

        // Orders reference customers with a restricted FK, so a customer with history cannot be removed
        if (await _customerRepository.HasOrdersAsync(id))
        {
            throw new ConflictException($"Customer {id} has orders and cannot be deleted.");
        }

        _customerRepository.Delete(existing);
        await _customerRepository.SaveChangesAsync();

        return true;
    }
}
