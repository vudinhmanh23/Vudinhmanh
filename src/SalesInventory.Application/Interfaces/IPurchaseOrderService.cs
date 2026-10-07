using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Interfaces;

// Business-facing operations for PurchaseOrder (stock-in), on top of the repository layer
public interface IPurchaseOrderService
{
    // Optional page/pageSize limit the list (newest first); without them every order is returned
    Task<IReadOnlyList<PurchaseOrder>> GetPurchaseOrdersAsync(int? page = null, int? pageSize = null);
    Task<int> CountPurchaseOrdersAsync();
    Task<PurchaseOrder?> GetPurchaseOrderAsync(int id);

    // Computes totals and saves the order as a Draft; stock is not touched until it is approved
    Task<PurchaseOrder> CreatePurchaseOrderAsync(PurchaseOrder purchaseOrder);

    // Draft -> Approved: increases stock and logs one Purchase movement per line, all in one transaction
    Task<PurchaseOrder> ApprovePurchaseOrderAsync(int id);

    // Approved -> Cancelled: takes the added stock back and logs one negative Adjustment movement per line, in one transaction
    Task<PurchaseOrder> CancelPurchaseOrderAsync(int id);

    // Removes the order; an Approved order also has its stock effect reversed, in one transaction
    Task<bool> DeletePurchaseOrderAsync(int id);
}
