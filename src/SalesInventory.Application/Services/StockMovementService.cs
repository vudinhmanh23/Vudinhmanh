using SalesInventory.Application.Interfaces;
using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Services;

public class StockMovementService : IStockMovementService
{
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
}
