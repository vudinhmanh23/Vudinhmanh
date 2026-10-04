using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Interfaces;

public interface IStockMovementRepository : IRepository<StockMovement>
{
    // Movements of one product, newest first
    Task<IReadOnlyList<StockMovement>> GetByProductAsync(int productId);
}
