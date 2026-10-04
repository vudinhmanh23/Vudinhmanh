using Microsoft.EntityFrameworkCore;
using SalesInventory.Application.Interfaces;
using SalesInventory.Domain.Entities;
using SalesInventory.Infrastructure.Persistence;

namespace SalesInventory.Infrastructure.Repositories;

public class StockMovementRepository : Repository<StockMovement>, IStockMovementRepository
{
    public StockMovementRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<StockMovement>> GetByProductAsync(int productId)
    {
        // Id breaks ties between rows written in the same instant (e.g. the lines of one approval)
        return await _context.StockMovements
            .AsNoTracking()
            .Where(m => m.ProductId == productId)
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .ToListAsync();
    }
}
