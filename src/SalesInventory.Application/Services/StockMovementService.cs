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
    private readonly IUnitOfWork _unitOfWork;

    public StockMovementService(
        IStockMovementRepository stockMovementRepository,
        IRepository<Product> productRepository,
        IUnitOfWork unitOfWork)
    {
        _stockMovementRepository = stockMovementRepository;
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
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

        RequireReason(reason);

        // long arithmetic so a huge delta cannot wrap around into a "valid" number
        return await ApplyAsync(productId, reason, previous => (long)previous + delta);
    }

    public async Task<StockAdjustmentResult> SetStockAsync(int productId, int newQuantity, string reason)
    {
        if (newQuantity < 0)
        {
            throw Invalid("NewQuantity", "The new stock quantity must not be negative.");
        }

        RequireReason(reason);

        return await ApplyAsync(productId, reason, _ => newQuantity);
    }

    private static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw Invalid("Reason", "A reason is required for a stock adjustment.");
        }
    }

    // Shared by both adjustment flavours: the target is computed from the stock the row has right now,
    // so Quantity = new - old always matches what was really changed
    private async Task<StockAdjustmentResult> ApplyAsync(int productId, string reason, Func<int, long> computeTarget)
    {
        // Product read, stock change and ledger row are one all-or-nothing unit
        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var product = await _productRepository.GetByIdAsync(productId)
                ?? throw new NotFoundException($"Product with Id {productId} does not exist.");

            var previous = product.StockQuantity;
            var next = computeTarget(previous);
            if (next < 0)
            {
                throw new ConflictException(
                    $"Adjustment of {next - previous} would leave {product.Name} (Id {product.Id}) with negative stock: {previous} in stock.");
            }

            var change = (int)(next - previous);
            if (change == 0)
            {
                throw Invalid("NewQuantity", $"{product.Name} (Id {product.Id}) already has {previous} in stock; nothing to adjust.");
            }

            product.StockQuantity = (int)next;

            var movement = new StockMovement
            {
                ProductId = productId,
                MovementType = StockMovementType.Adjustment,
                Quantity = change,
                StockAfter = product.StockQuantity,
                RefType = ManualAdjustmentReferenceType,
                RefId = 0, // no source document for a manual correction
                CreatedAt = DateTime.UtcNow,
                Note = reason.Trim()
            };
            await _stockMovementRepository.AddAsync(movement);

            // A concurrent change of the same product trips Product.RowVersion here (mapped to HTTP 409)
            await _stockMovementRepository.SaveChangesAsync();
            await transaction.CommitAsync();

            return new StockAdjustmentResult(movement, previous, product.StockQuantity);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static ValidationException Invalid(string property, string message)
    {
        return new ValidationException(new[] { new ValidationFailure(property, message) });
    }
}
