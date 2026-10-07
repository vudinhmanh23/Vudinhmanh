using Microsoft.Extensions.Logging;
using SalesInventory.Domain.Entities;
using SalesInventory.Domain.Enums;
using SalesInventory.Application.Exceptions;
using SalesInventory.Application.Interfaces;

namespace SalesInventory.Application.Services;

public class PurchaseOrderService : IPurchaseOrderService
{
    public const string PurchaseOrderReferenceType = "PurchaseOrder";

    private readonly IPurchaseOrderRepository _purchaseOrderRepository;
    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IProductRepository _productRepository;
    private readonly IRepository<StockMovement> _stockMovementRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PurchaseOrderService> _logger;

    public PurchaseOrderService(
        IPurchaseOrderRepository purchaseOrderRepository,
        IRepository<Supplier> supplierRepository,
        IProductRepository productRepository,
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

    public async Task<IReadOnlyList<PurchaseOrder>> GetPurchaseOrdersAsync(int? page = null, int? pageSize = null)
    {
        return await _purchaseOrderRepository.GetAllWithItemsAsync(page, pageSize);
    }

    public async Task<int> CountPurchaseOrdersAsync()
    {
        return await _purchaseOrderRepository.CountAsync();
    }

    public async Task<PurchaseOrder?> GetPurchaseOrderAsync(int id)
    {
        return await _purchaseOrderRepository.GetForReadAsync(id);
    }

    public async Task<PurchaseOrder> CreatePurchaseOrderAsync(PurchaseOrder purchaseOrder)
    {
        var items = purchaseOrder.PurchaseOrderItems.ToList();
        if (items.Count == 0)
        {
            throw new BusinessRuleException("A purchase order must contain at least one item.");
        }

        if (items.Sum(i => i.Quantity) <= 0)
        {
            throw new BusinessRuleException("Total quantity of a purchase order must be greater than 0.");
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
            throw new NotFoundException($"Không tìm thấy nhà cung cấp (Id {purchaseOrder.SupplierId}).");
        }

        // Every product must exist; report all missing ids at once
        var requestedProductIds = items.Select(i => i.ProductId).Distinct().ToList();
        var existingProductIds = await _productRepository.GetExistingIdsAsync(requestedProductIds);
        var missingProductIds = requestedProductIds.Except(existingProductIds).ToList();

        if (missingProductIds.Count > 0)
        {
            throw new NotFoundException($"Product(s) with Id {string.Join(", ", missingProductIds)} do not exist.");
        }

        // Never trust client-supplied money: totals are recomputed here
        foreach (var item in items)
        {
            item.LineTotal = item.Quantity * item.UnitPrice;
        }

        purchaseOrder.TotalAmount = items.Sum(i => i.LineTotal);
        purchaseOrder.Status = PurchaseOrderStatus.Draft;
        purchaseOrder.Code = await GenerateCodeAsync(DateTime.UtcNow);

        // A draft does not touch stock, so a single SaveChanges (atomic by itself) is enough here
        await _purchaseOrderRepository.AddAsync(purchaseOrder);
        await _purchaseOrderRepository.SaveChangesAsync();

        _logger.LogInformation(
            "Purchase order {PurchaseOrderId} ({Code}) created as Draft for supplier {SupplierId}: {ItemCount} line(s), total {TotalAmount}",
            purchaseOrder.Id, purchaseOrder.Code, purchaseOrder.SupplierId, items.Count, purchaseOrder.TotalAmount);

        return purchaseOrder;
    }

    public async Task<PurchaseOrder> ApprovePurchaseOrderAsync(int id)
    {
        var order = await _purchaseOrderRepository.GetWithItemsAsync(id)
            ?? throw new NotFoundException($"Không tìm thấy phiếu nhập có Id {id}.");

        // Only a Draft can be approved; anything else is a state conflict (HTTP 409)
        if (order.Status != PurchaseOrderStatus.Draft)
        {
            throw new ConflictException(
                $"Không thể duyệt phiếu nhập {order.Code}: phiếu đang ở trạng thái \"{StatusLabel(order.Status)}\". Chỉ có thể duyệt phiếu ở trạng thái \"{StatusLabel(PurchaseOrderStatus.Draft)}\".");
        }

        // An order without lines would be "approved" while changing nothing, so refuse it (HTTP 400)
        if (order.PurchaseOrderItems.Count == 0)
        {
            throw new BusinessRuleException(
                $"Không thể duyệt phiếu nhập {order.Code}: phiếu không có dòng hàng nào.");
        }

        // Everything below is one all-or-nothing unit: stock, movements and status change together
        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var now = DateTime.UtcNow;

            // One stock increase and one Purchase movement for EVERY line (the products are tracked via Include)
            foreach (var item in order.PurchaseOrderItems)
            {
                var product = item.Product
                    ?? throw new InvalidOperationException($"Product {item.ProductId} of purchase order {order.Id} is missing.");

                product.StockQuantity += item.Quantity;

                await _stockMovementRepository.AddAsync(new StockMovement
                {
                    ProductId = item.ProductId,
                    MovementType = StockMovementType.Import,
                    Quantity = item.Quantity,
                    StockAfter = product.StockQuantity,
                    Reference = order.Code,
                    RefType = PurchaseOrderReferenceType,
                    RefId = order.Id,
                    CreatedAt = now,
                    Note = $"Approved purchase order {order.Code}"
                });
            }

            order.Status = PurchaseOrderStatus.Approved;

            await _purchaseOrderRepository.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        _logger.LogInformation(
            "Purchase order {PurchaseOrderId} ({Code}) approved: {ItemCount} line(s), total {TotalAmount}",
            order.Id, order.Code, order.PurchaseOrderItems.Count, order.TotalAmount);

        return order;
    }

    public async Task<PurchaseOrder> CancelPurchaseOrderAsync(int id)
    {
        var order = await _purchaseOrderRepository.GetWithItemsAsync(id)
            ?? throw new NotFoundException($"Không tìm thấy phiếu nhập có Id {id}.");

        // Only an Approved order has added stock that can be taken back
        if (order.Status != PurchaseOrderStatus.Approved)
        {
            throw new ConflictException(
                $"Không thể hủy phiếu nhập {order.Code}: phiếu đang ở trạng thái \"{StatusLabel(order.Status)}\". Chỉ có thể hủy phiếu ở trạng thái \"{StatusLabel(PurchaseOrderStatus.Approved)}\".");
        }

        // Stock take-back, Adjustment movements and the status change succeed or fail together
        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            await ReverseStockAsync(order, StockMovementType.Adjustment, $"Cancelled purchase order {order.Code}", "hủy");

            order.Status = PurchaseOrderStatus.Cancelled;

            await _purchaseOrderRepository.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        _logger.LogInformation(
            "Purchase order {PurchaseOrderId} ({Code}) cancelled: stock reversed for {ItemCount} line(s)",
            order.Id, order.Code, order.PurchaseOrderItems.Count);

        return order;
    }

    public async Task<bool> DeletePurchaseOrderAsync(int id)
    {
        var existing = await _purchaseOrderRepository.GetWithItemsAsync(id);
        if (existing is null)
        {
            return false;
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            // Only an Approved order ever added stock, so only that case has anything to take back
            if (existing.Status == PurchaseOrderStatus.Approved)
            {
                await ReverseStockAsync(existing, StockMovementType.Sale, $"Deleted approved purchase order {existing.Code}", "xóa");
            }

            // Line items are removed by the cascade delete on the foreign key
            _purchaseOrderRepository.Delete(existing);
            await _purchaseOrderRepository.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return true;
    }

    // Takes every line's quantity back out of stock and logs one negative movement per line.
    // Callers must run this inside a transaction; nothing is saved here.
    private async Task ReverseStockAsync(PurchaseOrder order, StockMovementType movementType, string note, string actionLabel)
    {
        var now = DateTime.UtcNow;
        foreach (var item in order.PurchaseOrderItems)
        {
            var product = item.Product
                ?? throw new InvalidOperationException($"Product {item.ProductId} of purchase order {order.Id} is missing.");

            // Stock cannot go negative: part of the received goods may already have been sold
            if (product.StockQuantity < item.Quantity)
            {
                throw new BusinessRuleException(
                    $"Không thể {actionLabel} phiếu nhập {order.Code}: sản phẩm {product.Id} chỉ còn {product.StockQuantity} trong kho nhưng cần trừ lại {item.Quantity}.");
            }

            product.StockQuantity -= item.Quantity;

            await _stockMovementRepository.AddAsync(new StockMovement
            {
                ProductId = item.ProductId,
                MovementType = movementType,
                Quantity = -item.Quantity,
                StockAfter = product.StockQuantity,
                Reference = order.Code,
                RefType = PurchaseOrderReferenceType,
                RefId = order.Id,
                CreatedAt = now,
                Note = note
            });
        }
    }

    // PO-yyyyMMdd-NNN: NNN restarts at 001 each day. The unique index on Code is the final guard against duplicates.
    private async Task<string> GenerateCodeAsync(DateTime now)
    {
        var prefix = $"PO-{now:yyyyMMdd}-";
        var last = await _purchaseOrderRepository.GetLastCodeWithPrefixAsync(prefix);
        var next = last is not null && int.TryParse(last[prefix.Length..], out var n) ? n + 1 : 1;
        return $"{prefix}{next:000}";
    }

    // Vietnamese label used in user-facing messages
    private static string StatusLabel(PurchaseOrderStatus status) => status switch
    {
        PurchaseOrderStatus.Draft => "Nháp",
        PurchaseOrderStatus.Approved => "Đã duyệt",
        PurchaseOrderStatus.Cancelled => "Đã hủy",
        _ => status.ToString()
    };
}
