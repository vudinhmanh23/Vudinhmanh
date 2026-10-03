using Microsoft.Extensions.Logging;
using SalesInventory.Domain.Entities;
using SalesInventory.Application.Exceptions;
using SalesInventory.Application.Interfaces;

namespace SalesInventory.Application.Services;

public class PurchaseOrderService : IPurchaseOrderService
{
    public const string PurchaseReason = "Purchase";
    public const string PurchaseCancelReason = "PurchaseCancel";

    private readonly IPurchaseOrderRepository _purchaseOrderRepository;
    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<Product> _productRepository;
    private readonly IRepository<StockMovement> _stockMovementRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PurchaseOrderService> _logger;

    public PurchaseOrderService(
        IPurchaseOrderRepository purchaseOrderRepository,
        IRepository<Supplier> supplierRepository,
        IRepository<Product> productRepository,
        IRepository<StockMovement> stockMovementRepository,
        IUnitOfWork unitOfWork,
        ILogger<PurchaseOrderService> logger)
    {
        _logger = logger;
        _purchaseOrderRepository = purchaseOrderRepository;
        _supplierRepository = supplierRepository;
        _productRepository = productRepository;
        _stockMovementRepository = stockMovementRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<PurchaseOrder>> GetPurchaseOrdersAsync()
    {
        return await _purchaseOrderRepository.GetAllWithItemsAsync();
    }

    public async Task<PurchaseOrder?> GetPurchaseOrderAsync(int id)
    {
        return await _purchaseOrderRepository.GetWithItemsAsync(id);
    }

    public async Task<PurchaseOrder> CreatePurchaseOrderAsync(PurchaseOrder purchaseOrder)
    {
        var items = purchaseOrder.PurchaseOrderItems.ToList();
        if (items.Count == 0)
        {
            throw new BusinessRuleException("A purchase order must contain at least one item.");
        }

        var badQuantity = items.FirstOrDefault(i => i.Quantity <= 0);
        if (badQuantity is not null)
        {
            throw new BusinessRuleException(
                $"Quantity must be greater than 0 (product {badQuantity.ProductId} has quantity {badQuantity.Quantity}).");
        }

        // Business rule: SupplierId must reference an existing supplier
        if (await _supplierRepository.GetByIdAsync(purchaseOrder.SupplierId) is null)
        {
            throw new NotFoundException($"Supplier with Id {purchaseOrder.SupplierId} does not exist.");
        }

        // Load every product first (tracked), so a missing product fails before anything is written.
        // Repeated ProductIds resolve to the same tracked instance, so their quantities accumulate.
        var products = new Dictionary<int, Product>();
        var missingProductIds = new List<int>();
        foreach (var productId in items.Select(i => i.ProductId).Distinct())
        {
            var product = await _productRepository.GetByIdAsync(productId);
            if (product is null)
            {
                missingProductIds.Add(productId);
            }
            else
            {
                products[productId] = product;
            }
        }

        if (missingProductIds.Count > 0)
        {
            throw new NotFoundException($"Product(s) with Id {string.Join(", ", missingProductIds)} do not exist.");
        }

        // All-or-nothing: disposing the transaction without commit rolls everything back
        await using var transaction = await _unitOfWork.BeginTransactionAsync();

        var now = DateTime.UtcNow;
        var movements = new List<StockMovement>();

        purchaseOrder.Code = await GenerateCodeAsync(now);

        // Never trust client-supplied money: totals are recomputed here
        foreach (var item in items)
        {
            item.LineTotal = item.Quantity * item.UnitPrice;
            products[item.ProductId].StockQuantity += item.Quantity;

            movements.Add(new StockMovement
            {
                ProductId = item.ProductId,
                ChangeQuantity = item.Quantity,
                Reason = PurchaseReason,
                CreatedAt = now
            });
        }
        purchaseOrder.TotalAmount = items.Sum(i => i.LineTotal);

        // First save generates PurchaseOrder.Id (children are saved with it via the navigation collection)
        await _purchaseOrderRepository.AddAsync(purchaseOrder);
        await _purchaseOrderRepository.SaveChangesAsync();

        // RefId needs the generated order Id, so the movements are saved in a second step of the same transaction
        foreach (var movement in movements)
        {
            movement.RefId = purchaseOrder.Id;
            await _stockMovementRepository.AddAsync(movement);
        }
        await _stockMovementRepository.SaveChangesAsync();

        await transaction.CommitAsync();

        _logger.LogInformation(
            "Purchase order {PurchaseOrderId} ({Code}) created for supplier {SupplierId}: {ItemCount} line(s), total {TotalAmount}",
            purchaseOrder.Id, purchaseOrder.Code, purchaseOrder.SupplierId, items.Count, purchaseOrder.TotalAmount);

        return purchaseOrder;
    }

    // PO-yyyyMMdd-NNN: NNN restarts at 001 each day. The unique index on Code is the final guard against duplicates.
    private async Task<string> GenerateCodeAsync(DateTime now)
    {
        var prefix = $"PO-{now:yyyyMMdd}-";
        var last = await _purchaseOrderRepository.GetLastCodeWithPrefixAsync(prefix);
        var next = last is not null && int.TryParse(last[prefix.Length..], out var n) ? n + 1 : 1;
        return $"{prefix}{next:000}";
    }

    public async Task<bool> DeletePurchaseOrderAsync(int id)
    {
        var existing = await _purchaseOrderRepository.GetWithItemsAsync(id);
        if (existing is null)
        {
            return false;
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync();

        // Deleting a receipt takes the goods back out of stock, and the ledger records it
        var now = DateTime.UtcNow;
        foreach (var item in existing.PurchaseOrderItems)
        {
            var product = await _productRepository.GetByIdAsync(item.ProductId)
                ?? throw new InvalidOperationException($"Product with Id {item.ProductId} no longer exists.");

            if (product.StockQuantity < item.Quantity)
            {
                throw new BusinessRuleException(
                    $"Cannot delete purchase order {id}: product {product.Id} has only {product.StockQuantity} in stock but {item.Quantity} would be removed.");
            }

            product.StockQuantity -= item.Quantity;

            await _stockMovementRepository.AddAsync(new StockMovement
            {
                ProductId = item.ProductId,
                ChangeQuantity = -item.Quantity,
                Reason = PurchaseCancelReason,
                RefId = id,
                CreatedAt = now
            });
        }

        // Line items are removed by the cascade delete on the foreign key
        _purchaseOrderRepository.Delete(existing);
        await _purchaseOrderRepository.SaveChangesAsync();

        await transaction.CommitAsync();

        return true;
    }
}
