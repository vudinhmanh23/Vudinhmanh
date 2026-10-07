using SalesInventory.Application.Dtos;

namespace SalesInventory.Application.Interfaces;

public interface IRevenueExcelService
{
    // Builds the workbook and returns the .xlsx bytes: sheet "DoanhThu" from daily revenue rows (Period = yyyy-MM-dd)
    // and sheet "TopSanPham" from the best-selling products
    byte[] Generate(IReadOnlyList<RevenueByPeriodDto> days, IReadOnlyList<TopProductDto> topProducts);
}
