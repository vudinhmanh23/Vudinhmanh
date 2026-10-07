using SalesInventory.Application.Dtos;

namespace SalesInventory.Application.Interfaces;

public interface IRevenuePdfService
{
    // Renders the daily revenue table (Period = yyyy-MM-dd) with a total row and returns the PDF bytes
    byte[] Generate(IReadOnlyList<RevenueByPeriodDto> days, DateTime from, DateTime to);
}
