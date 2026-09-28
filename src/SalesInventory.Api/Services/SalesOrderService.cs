using SalesInventory.Api.Models;
using SalesInventory.Api.Repositories;

namespace SalesInventory.Api.Services;

public class SalesOrderService : ISalesOrderService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderItem> _orderItemRepository;
    private readonly IRepository<Customer> _customerRepository;
    private readonly IRepository<Product> _productRepository;

    public SalesOrderService(
        IRepository<Order> orderRepository,
        IRepository<OrderItem> orderItemRepository,
        IRepository<Customer> customerRepository,
        IRepository<Product> productRepository)
    {
        _orderRepository = orderRepository;
        _orderItemRepository = orderItemRepository;
        _customerRepository = customerRepository;
        _productRepository = productRepository;
    }

    public async Task<IEnumerable<Order>> GetOrdersAsync()
    {
        return await _orderRepository.GetAllAsync();
    }

    public async Task<Order?> GetOrderAsync(int id)
    {
        return await _orderRepository.GetByIdAsync(id);
    }

    public async Task<IEnumerable<OrderItem>> GetOrderItemsAsync()
    {
        return await _orderItemRepository.GetAllAsync();
    }

    public async Task<Order> CreateOrderAsync(Order order, IEnumerable<OrderItem> items)
    {
        // Business rule: CustomerId must reference an existing customer
        if (await _customerRepository.GetByIdAsync(order.CustomerId) is null)
        {
            throw new ArgumentException($"Customer with Id {order.CustomerId} does not exist.", nameof(order));
        }

        var itemList = items.ToList();
        if (itemList.Count == 0)
        {
            throw new ArgumentException("A sales order must contain at least one item.", nameof(items));
        }

        foreach (var item in itemList)
        {
            if (item.Quantity <= 0)
            {
                throw new ArgumentException("Item quantity must be greater than zero.", nameof(items));
            }

            if (await _productRepository.GetByIdAsync(item.ProductId) is null)
            {
                throw new ArgumentException($"Product with Id {item.ProductId} does not exist.", nameof(items));
            }
        }

        await _orderRepository.AddAsync(order);
        await _orderRepository.SaveChangesAsync();

        foreach (var item in itemList)
        {
            item.OrderId = order.Id;
            await _orderItemRepository.AddAsync(item);
        }
        await _orderItemRepository.SaveChangesAsync();

        return order;
    }

    public async Task<bool> DeleteOrderAsync(int id)
    {
        var existing = await _orderRepository.GetByIdAsync(id);
        if (existing is null)
        {
            return false;
        }

        var items = await _orderItemRepository.GetAllAsync();
        foreach (var item in items.Where(i => i.OrderId == id))
        {
            _orderItemRepository.Delete(item);
        }
        await _orderItemRepository.SaveChangesAsync();

        _orderRepository.Delete(existing);
        await _orderRepository.SaveChangesAsync();

        return true;
    }
}
