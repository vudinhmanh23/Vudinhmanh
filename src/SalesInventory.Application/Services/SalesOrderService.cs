using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesInventory.Application.Exceptions;
using SalesInventory.Application.Interfaces;
using SalesInventory.Domain.Entities;
using SalesInventory.Domain.Enums;

namespace SalesInventory.Application.Services;

public class SalesOrderService : ISalesOrderService
{
    public const string SalesOrderReferenceType = "SalesOrder";

    private readonly ISalesOrderRepository _orderRepository;
    private readonly IRepository<Customer> _customerRepository;
    private readonly IRepository<Product> _productRepository;
    private readonly IRepository<StockMovement> _stockMovementRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly InventorySettings _settings;
    private readonly ILogger<SalesOrderService> _logger;

    public SalesOrderService(
        ISalesOrderRepository orderRepository,
        IRepository<Customer> customerRepository,
        IRepository<Product> productRepository,
        IRepository<StockMovement> stockMovementRepository,
        IUnitOfWork unitOfWork,
        IOptions<InventorySettings> settings,
        ILogger<SalesOrderService> logger)
    {
        _orderRepository = orderRepository;
        _customerRepository = customerRepository;
        _productRepository = productRepository;
        _stockMovementRepository = stockMovementRepository;
        _unitOfWork = unitOfWork;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SalesOrder>> GetOrdersAsync()
    {
        return await _orderRepository.GetAllWithItemsAsync();
    }

    public async Task<IReadOnlyList<SalesOrder>> GetOrdersByCustomerAsync(int customerId)
    {
        return await _orderRepository.GetByCustomerWithItemsAsync(customerId);
    }

    public async Task<SalesOrder?> GetOrderAsync(int id)
    {
        return await _orderRepository.GetWithItemsAsync(id);
    }

    public async Task<CreateSalesOrderResult> CreateOrderAsync(SalesOrder order)
    {
        var items = order.Items.ToList();
        if (items.Count == 0)
        {
            throw Invalid("Items", "A sales order must contain at least one item.");
        }

        if (items.Any(i => i.Quantity <= 0))
        {
            throw Invalid("Items", "Item quantity must be greater than zero.");
        }

        if (items.Any(i => i.UnitPrice < 0) || order.DiscountAmount < 0)
        {
            throw Invalid("DiscountAmount", "Prices and discount cannot be negative.");
        }

        if (await _customerRepository.GetByIdAsync(order.CustomerId) is null)
        {
            throw new NotFoundException($"Customer with Id {order.CustomerId} does not exist.");
        }

        // Never trust client-supplied money: line totals and the order total are recomputed here
        foreach (var item in items)
        {
            item.LineTotal = Math.Round(item.Quantity * item.UnitPrice, 2, MidpointRounding.AwayFromZero);
        }

        order.DiscountAmount = Math.Round(order.DiscountAmount, 2, MidpointRounding.AwayFromZero);
        order.TotalAmount = items.Sum(i => i.LineTotal) - order.DiscountAmount;
        if (order.TotalAmount < 0)
        {
            throw Invalid("DiscountAmount", "Discount cannot exceed the sum of the line totals.");
        }

        // Load every product (tracked) and check the WHOLE order against stock before changing anything.
        // Lines of the same product are summed so two lines cannot each pass while together overselling.
        var products = new Dictionary<int, Product>();
        foreach (var productId in items.Select(i => i.ProductId).Distinct())
        {
            products[productId] = await _productRepository.GetByIdAsync(productId)
                ?? throw new NotFoundException($"Product with Id {productId} does not exist.");
        }

        var shortages = items
            .GroupBy(i => i.ProductId)
            .Select(g => (Product: products[g.Key], Requested: g.Sum(i => i.Quantity)))
            .Where(x => x.Requested > x.Product.StockQuantity)
            .Select(x => $"{x.Product.Name} (Id {x.Product.Id}): requested {x.Requested}, in stock {x.Product.StockQuantity}")
            .ToList();
        if (shortages.Count > 0)
        {
            throw new ConflictException($"Insufficient stock: {string.Join("; ", shortages)}.");
        }

        // From here on it is one all-or-nothing unit: order, stock deduction and ledger rows
        await using var transaction = await _unitOfWork.BeginTransactionAsync();

        await _orderRepository.AddAsync(order);
        await _orderRepository.SaveChangesAsync(); // gives the order its Id for the movement reference

        var now = DateTime.UtcNow;
        foreach (var item in items)
        {
            products[item.ProductId].StockQuantity -= item.Quantity;

            await _stockMovementRepository.AddAsync(new StockMovement
            {
                ProductId = item.ProductId,
                MovementType = StockMovementType.Sale,
                Quantity = -item.Quantity,
                ReferenceType = SalesOrderReferenceType,
                ReferenceId = order.Id,
                CreatedAt = now,
                Note = $"Sales order {order.Id}"
            });
        }

        // A concurrent sale of the same product trips Product.RowVersion here (mapped to HTTP 409)
        await _orderRepository.SaveChangesAsync();
        await transaction.CommitAsync();

        var warnings = products.Values
            .Where(p => p.StockQuantity < _settings.LowStockThreshold)
            .Select(p => $"Low stock: {p.Name} (Id {p.Id}) has {p.StockQuantity} left, below the threshold of {_settings.LowStockThreshold}.")
            .ToList();

        _logger.LogInformation(
            "Sales order {OrderId} created for customer {CustomerId}: {ItemCount} line(s), total {TotalAmount}, {WarningCount} low-stock warning(s)",
            order.Id, order.CustomerId, items.Count, order.TotalAmount, warnings.Count);

        return new CreateSalesOrderResult(order, warnings);
    }

    public async Task<bool> DeleteOrderAsync(int id)
    {
        var existing = await _orderRepository.GetByIdAsync(id);
        if (existing is null)
        {
            return false;
        }

        // Items are removed by the cascade delete
        _orderRepository.Delete(existing);
        await _orderRepository.SaveChangesAsync();

        return true;
    }

    // FluentValidation ValidationException is what the API middleware turns into a 400 ProblemDetails
    private static ValidationException Invalid(string property, string message)
    {
        return new ValidationException(new[] { new ValidationFailure(property, message) });
    }
}
