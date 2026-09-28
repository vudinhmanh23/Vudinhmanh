using SalesInventory.Api.Models;

namespace SalesInventory.Api.Services;

// Business-facing operations for PurchaseOrder (stock-in), on top of the repository layer
public interface IPurchaseOrderService
{
    Task<IEnumerable<PurchaseOrder>> GetPurchaseOrdersAsync();
    Task<PurchaseOrder?> GetPurchaseOrderAsync(int id);
    Task<IEnumerable<PurchaseOrderItem>> GetPurchaseOrderItemsAsync();
    Task<PurchaseOrder> CreatePurchaseOrderAsync(PurchaseOrder purchaseOrder, IEnumerable<PurchaseOrderItem> items);
    Task<bool> DeletePurchaseOrderAsync(int id);
}
