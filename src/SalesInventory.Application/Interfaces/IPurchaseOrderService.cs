using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Interfaces;

// Business-facing operations for PurchaseOrder (stock-in), on top of the repository layer
public interface IPurchaseOrderService
{
    Task<IReadOnlyList<PurchaseOrder>> GetPurchaseOrdersAsync();
    Task<PurchaseOrder?> GetPurchaseOrderAsync(int id);

    // Computes totals, increases product stock and logs stock movements in one transaction
    Task<PurchaseOrder> CreatePurchaseOrderAsync(PurchaseOrder purchaseOrder);

    // Removes the order and reverses its stock effect in one transaction
    Task<bool> DeletePurchaseOrderAsync(int id);
}
