using FluentValidation;
using FluentValidation.Results;
using SalesInventory.Application.Exceptions;
using SalesInventory.Application.Interfaces;
using SalesInventory.Domain.Entities;
using SalesInventory.Domain.Enums;

namespace SalesInventory.Application.Services;

public class StockMovementService : IStockMovementService
{
    public const string ManualAdjustmentReferenceType = "ManualAdjustment";

    private readonly IStockMovementRepository _stockMovementRepository;
    private readonly IRepository<Product> _productRepository;

    public StockMovementService(IStockMovementRepository stockMovementRepository, IRepository<Product> productRepository)
    {
        _stockMovementRepository = stockMovementRepository;
        _productRepository = productRepository;
    }

    public async Task<IReadOnlyList<StockMovement>?> GetProductMovementsAsync(int productId)
    {
        if (await _productRepository.GetByIdAsync(productId) is null)
        {
            return null;
        }

        return await _stockMovementRepository.GetByProductAsync(productId);
    }

    public async Task<StockAdjustmentResult> AdjustStockAsync(int productId, int delta, string reason)
    {
        if (delta == 0)
        {
            throw Invalid("Delta", "Delta must not be zero.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw Invalid("Reason", "A reason is required for a stock adjustment.");
        }

        var product = await _productRepository.GetByIdAsync(productId)
            ?? throw new NotFoundException($"Product with Id {productId} does not exist.");

        var previous = product.StockQuantity;
        // long arithmetic so a huge delta cannot wrap around into a "valid" number
        var next = (long)previous + delta;
        if (next < 0)
        {
            throw new ConflictException(
                $"Adjustment of {delta} would leave {product.Name} (Id {product.Id}) with negative stock: {previous} in stock.");
        }

        product.StockQuantity = (int)next;

        var movement = new StockMovement
        {
            ProductId = productId,
            MovementType = StockMovementType.Adjustment,
            Quantity = delta,
            ReferenceType = ManualAdjustmentReferenceType,
            ReferenceId = 0, // no source document for a manual correction
            CreatedAt = DateTime.UtcNow,
            Note = reason.Trim()
        };
        await _stockMovementRepository.AddAsync(movement);

        // One SaveChanges is one database transaction: the stock change and its ledger row commit together or not at all
        await _stockMovementRepository.SaveChangesAsync();

        return new StockAdjustmentResult(movement, previous, product.StockQuantity);
    }

    private static ValidationException Invalid(string property, string message)
    {
        return new ValidationException(new[] { new ValidationFailure(property, message) });
    }
}
