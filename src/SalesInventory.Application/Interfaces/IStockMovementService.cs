using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Interfaces;

// The movement that was written plus the stock level before and after it
public record StockAdjustmentResult(StockMovement Movement, int PreviousQuantity, int NewQuantity);

public interface IStockMovementService
{
    // Newest first; null when the product does not exist
    Task<IReadOnlyList<StockMovement>?> GetProductMovementsAsync(int productId);

    // Manual correction of a product's stock by a signed delta, logged as an Adjustment movement.
    // Throws NotFoundException (unknown product), a validation error (zero delta / blank reason)
    // or ConflictException when the result would be below zero.
    Task<StockAdjustmentResult> AdjustStockAsync(int productId, int delta, string reason);
}
