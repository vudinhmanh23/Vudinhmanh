using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Interfaces;

public interface IStockMovementService
{
    // Newest first; null when the product does not exist
    Task<IReadOnlyList<StockMovement>?> GetProductMovementsAsync(int productId);
}
