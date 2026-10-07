using SalesInventory.Application.Dtos;

namespace SalesInventory.Application.Interfaces;

public interface IDashboardService
{
    // With both dates, also compares revenue with the equally long period right before `from`.
    // `to` without a time part (midnight) covers that whole day; with a time part it is inclusive up to that instant
    Task<DashboardSummaryDto> GetSummaryAsync(DateTime? from, DateTime? to);

    Task<IReadOnlyList<DashboardLowStockItemDto>> GetLowStockItemsAsync(int limit);
}
