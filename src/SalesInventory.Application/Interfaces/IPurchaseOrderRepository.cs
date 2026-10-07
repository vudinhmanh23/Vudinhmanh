using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Interfaces;

// Purchase-order queries that need the line items (and their products) loaded
public interface IPurchaseOrderRepository : IRepository<PurchaseOrder>
{
    // Tracked, whole entities: for code that changes the order
    Task<PurchaseOrder?> GetWithItemsAsync(int id);

    // Read-only (not tracked, product names only) version of GetWithItemsAsync
    Task<PurchaseOrder?> GetForReadAsync(int id);
    // Read-only; optional page/pageSize limit the result, without them every order is returned
    Task<IReadOnlyList<PurchaseOrder>> GetAllWithItemsAsync(int? page = null, int? pageSize = null);

    // Highest existing Code that starts with the prefix (e.g. "PO-20260826-"), or null if none
    Task<string?> GetLastCodeWithPrefixAsync(string prefix);
}
