using SalesInventory.Application.Dtos;

namespace SalesInventory.Application.Interfaces;

public interface IRevenueExcelService
{
    // Builds the "DoanhThu" workbook from daily revenue rows (Period = yyyy-MM-dd) and returns the .xlsx bytes
    byte[] Generate(IReadOnlyList<RevenueByPeriodDto> days);
}
