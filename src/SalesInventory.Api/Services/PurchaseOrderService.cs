using SalesInventory.Api.Models;
using SalesInventory.Api.Repositories;

namespace SalesInventory.Api.Services;

public class PurchaseOrderService : IPurchaseOrderService
{
    private readonly IRepository<PurchaseOrder> _purchaseOrderRepository;
    private readonly IRepository<PurchaseOrderItem> _purchaseOrderItemRepository;
    private readonly IRepository<Supplier> _supplierRepository;
    private readonly IRepository<Product> _productRepository;

    public PurchaseOrderService(
        IRepository<PurchaseOrder> purchaseOrderRepository,
        IRepository<PurchaseOrderItem> purchaseOrderItemRepository,
        IRepository<Supplier> supplierRepository,
        IRepository<Product> productRepository)
    {
        _purchaseOrderRepository = purchaseOrderRepository;
        _purchaseOrderItemRepository = purchaseOrderItemRepository;
        _supplierRepository = supplierRepository;
        _productRepository = productRepository;
    }

    public async Task<IEnumerable<PurchaseOrder>> GetPurchaseOrdersAsync()
    {
        return await _purchaseOrderRepository.GetAllAsync();
    }

    public async Task<PurchaseOrder?> GetPurchaseOrderAsync(int id)
    {
        return await _purchaseOrderRepository.GetByIdAsync(id);
    }

    public async Task<IEnumerable<PurchaseOrderItem>> GetPurchaseOrderItemsAsync()
    {
        return await _purchaseOrderItemRepository.GetAllAsync();
    }

    public async Task<PurchaseOrder> CreatePurchaseOrderAsync(PurchaseOrder purchaseOrder, IEnumerable<PurchaseOrderItem> items)
    {
        // Business rule: SupplierId must reference an existing supplier
        if (await _supplierRepository.GetByIdAsync(purchaseOrder.SupplierId) is null)
        {
            throw new ArgumentException($"Supplier with Id {purchaseOrder.SupplierId} does not exist.", nameof(purchaseOrder));
        }

        var itemList = items.ToList();
        if (itemList.Count == 0)
        {
            throw new ArgumentException("A purchase order must contain at least one item.", nameof(items));
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

        await _purchaseOrderRepository.AddAsync(purchaseOrder);
        await _purchaseOrderRepository.SaveChangesAsync();

        foreach (var item in itemList)
        {
            item.PurchaseOrderId = purchaseOrder.Id;
            await _purchaseOrderItemRepository.AddAsync(item);
        }
        await _purchaseOrderItemRepository.SaveChangesAsync();

        return purchaseOrder;
    }

    public async Task<bool> DeletePurchaseOrderAsync(int id)
    {
        var existing = await _purchaseOrderRepository.GetByIdAsync(id);
        if (existing is null)
        {
            return false;
        }

        var items = await _purchaseOrderItemRepository.GetAllAsync();
        foreach (var item in items.Where(i => i.PurchaseOrderId == id))
        {
            _purchaseOrderItemRepository.Delete(item);
        }
        await _purchaseOrderItemRepository.SaveChangesAsync();

        _purchaseOrderRepository.Delete(existing);
        await _purchaseOrderRepository.SaveChangesAsync();

        return true;
    }
}
