using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Interfaces;

// Purchase-order queries that need the line items (and their products) loaded
public interface IPurchaseOrderRepository : IRepository<PurchaseOrder>
{
    Task<PurchaseOrder?> GetWithItemsAsync(int id);
    Task<IReadOnlyList<PurchaseOrder>> GetAllWithItemsAsync();

    // Highest existing Code that starts with the prefix (e.g. "PO-20260826-"), or null if none
    Task<string?> GetLastCodeWithPrefixAsync(string prefix);
}
