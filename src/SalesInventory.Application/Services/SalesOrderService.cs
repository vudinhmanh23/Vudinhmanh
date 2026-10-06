using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Exceptions;
using SalesInventory.Application.Interfaces;
using SalesInventory.Domain.Entities;
using SalesInventory.Domain.Enums;

namespace SalesInventory.Application.Services;

public class SalesOrderService : ISalesOrderService
{
    public const string SalesOrderReferenceType = "SalesOrder";
    private const int MaxPlaceAttempts = 5;

    private readonly ISalesOrderRepository _orderRepository;
    private readonly IRepository<Customer> _customerRepository;
    private readonly IRepository<Product> _productRepository;
    private readonly IRepository<StockMovement> _stockMovementRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SalesOrderService> _logger;

    public SalesOrderService(
        ISalesOrderRepository orderRepository,
        IRepository<Customer> customerRepository,
        IRepository<Product> productRepository,
        IRepository<StockMovement> stockMovementRepository,
        IUnitOfWork unitOfWork,
        ILogger<SalesOrderService> logger)
    {
        _orderRepository = orderRepository;
        _customerRepository = customerRepository;
        _productRepository = productRepository;
        _stockMovementRepository = stockMovementRepository;
        _unitOfWork = unitOfWork;
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
        // Cheap input checks come first: nothing below this point has opened a transaction yet
        if (items.Count == 0)
        {
            throw Invalid("Items", "Đơn hàng phải có ít nhất một mặt hàng");
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

        // Optimistic concurrency: a request that loses a race on the same stock rows (stale RowVersion, duplicate
        // order number, deadlock victim) is rolled back completely and re-run against the fresh stock. The retry
        // either succeeds or fails the normal stock check with 409, so stock can never go below zero.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await PlaceOrderAsync(order, items);
            }
            catch (ConcurrencyConflictException ex) when (attempt < MaxPlaceAttempts)
            {
                _logger.LogWarning(ex, "Sales order attempt {Attempt}/{Max} lost a concurrency race; retrying", attempt, MaxPlaceAttempts);

                _unitOfWork.ResetTracking();
                // Back to the shape the caller handed in: no generated keys, and no navigations that EF's
                // fix-up pointed at the (now discarded) tracked Customer/Product instances
                order.Id = 0;
                order.Customer = null;
                foreach (var item in items)
                {
                    item.Id = 0;
                    item.SalesOrderId = 0;
                    item.SalesOrder = null;
                    item.Product = null;
                }

                await Task.Delay(Random.Shared.Next(10, 40) * attempt);
            }
        }
    }

    // One all-or-nothing unit: stock check, order, stock deduction and ledger rows
    private async Task<CreateSalesOrderResult> PlaceOrderAsync(SalesOrder order, List<SalesOrderItem> items)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        var products = new Dictionary<int, Product>();
        try
        {
            // Load every product (tracked) and check the WHOLE order against stock before changing anything.
            // Lines of the same product are summed so two lines cannot each pass while together overselling.
            foreach (var productId in items.Select(i => i.ProductId).Distinct())
            {
                products[productId] = await _productRepository.GetByIdAsync(productId)
                    ?? throw new NotFoundException($"Product with Id {productId} does not exist.");
            }

            var shortages = items
                .GroupBy(i => i.ProductId)
                .Select(g => (Product: products[g.Key], Requested: g.Sum(i => i.Quantity)))
                .Where(x => x.Requested > x.Product.StockQuantity)
                .Select(x => new StockShortage(x.Product.Id, x.Product.Name, x.Requested, x.Product.StockQuantity))
                .ToList();
            if (shortages.Count > 0)
            {
                // HTTP 409 ProblemDetails naming each product, the quantity wanted and the stock on hand
                throw new InsufficientStockException(shortages);
            }

            order.OrderNumber = await GenerateOrderNumberAsync(DateTime.UtcNow);
            order.Status = SalesOrderStatus.Completed;

            await _orderRepository.AddAsync(order);
            await _orderRepository.SaveChangesAsync(); // gives the order its Id for the movement reference

            var now = DateTime.UtcNow;
            foreach (var item in items)
            {
                var product = products[item.ProductId];
                if (product.StockQuantity < item.Quantity)
                {
                    // Defensive: stock must never go negative
                    throw new InsufficientStockException(new[]
                    {
                        new StockShortage(product.Id, product.Name, item.Quantity, product.StockQuantity)
                    });
                }

                product.StockQuantity -= item.Quantity;

                await _stockMovementRepository.AddAsync(new StockMovement
                {
                    ProductId = item.ProductId,
                    MovementType = StockMovementType.Sale,
                    Quantity = -item.Quantity,
                    StockAfter = product.StockQuantity,
                    Reference = order.OrderNumber,
                    RefType = SalesOrderReferenceType,
                    RefId = order.Id,
                    CreatedAt = now,
                    Note = $"Sales order {order.OrderNumber}"
                });
            }

            // A concurrent sale of the same product trips Product.RowVersion here (mapped to HTTP 409)
            await _orderRepository.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            // Any failure undoes the order, the stock deduction and the ledger rows together
            await transaction.RollbackAsync();
            throw;
        }

        // Only products this order actually sold; the sale is already committed, so this never blocks it
        var lowStock = products.Values
            .Where(p => p.StockQuantity <= p.LowStockThreshold)
            .Select(p => new LowStockProductDto
            {
                ProductId = p.Id,
                ProductName = p.Name,
                StockQuantity = p.StockQuantity,
                LowStockThreshold = p.LowStockThreshold
            })
            .ToList();

        foreach (var low in lowStock)
        {
            _logger.LogWarning(
                "Low stock after sales order {OrderNumber}: product {ProductId} ({ProductName}) has {StockQuantity} left (threshold {LowStockThreshold})",
                order.OrderNumber, low.ProductId, low.ProductName, low.StockQuantity, low.LowStockThreshold);
        }

        var warnings = lowStock
            .Select(l => $"Low stock: {l.ProductName} (Id {l.ProductId}) has {l.StockQuantity} left, at or below the threshold of {l.LowStockThreshold}.")
            .ToList();

        _logger.LogInformation(
            "Sales order {OrderId} ({OrderNumber}) created for customer {CustomerId}: {ItemCount} line(s), total {TotalAmount}, {WarningCount} low-stock warning(s)",
            order.Id, order.OrderNumber, order.CustomerId, items.Count, order.TotalAmount, warnings.Count);

        return new CreateSalesOrderResult(order, warnings, lowStock);
    }

    private async Task<string> GenerateOrderNumberAsync(DateTime now)
    {
        var prefix = $"SO-{now:yyyyMMdd}-";
        var last = await _orderRepository.GetLastOrderNumberWithPrefixAsync(prefix);
        var next = last is not null && int.TryParse(last[prefix.Length..], out var n) ? n + 1 : 1;
        return $"{prefix}{next:0000}";
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
