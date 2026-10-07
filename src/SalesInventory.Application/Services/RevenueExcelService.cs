using System.Globalization;
using ClosedXML.Excel;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;

namespace SalesInventory.Application.Services;

public class RevenueExcelService : IRevenueExcelService
{
    private const string SheetName = "DoanhThu";
    private const string TopSheetName = "TopSanPham";

    // Vietnamese currency: thousands separators plus a trailing "đ"
    private const string CurrencyFormat = "#,##0\" đ\"";

    public byte[] Generate(IReadOnlyList<RevenueByPeriodDto> days, IReadOnlyList<TopProductDto> topProducts)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName);

        sheet.Cell(1, 1).Value = "Ngày";
        sheet.Cell(1, 2).Value = "Số đơn";
        sheet.Cell(1, 3).Value = "Doanh thu";
        var header = sheet.Range(1, 1, 1, 3);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;
        sheet.SheetView.FreezeRows(1);

        var row = 2;
        foreach (var day in days)
        {
            // Stored as real dates and numbers, not text, so Excel can sort, filter and sum them
            sheet.Cell(row, 1).Value = DateTime.ParseExact(day.Period, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            sheet.Cell(row, 2).Value = day.OrderCount;
            sheet.Cell(row, 3).Value = day.Revenue;
            row++;
        }

        var totalRow = row;
        sheet.Cell(totalRow, 1).Value = "Tổng cộng";
        if (days.Count > 0)
        {
            // Live formulas, so editing a row updates the total
            sheet.Cell(totalRow, 2).FormulaA1 = $"SUM(B2:B{totalRow - 1})";
            sheet.Cell(totalRow, 3).FormulaA1 = $"SUM(C2:C{totalRow - 1})";
        }
        else
        {
            sheet.Cell(totalRow, 2).Value = 0;
            sheet.Cell(totalRow, 3).Value = 0m;
        }

        sheet.Range(2, 1, totalRow, 1).Style.DateFormat.Format = "dd/MM/yyyy";
        sheet.Range(2, 1, totalRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        sheet.Range(2, 2, totalRow, 2).Style.NumberFormat.Format = "#,##0";
        sheet.Range(2, 3, totalRow, 3).Style.NumberFormat.Format = CurrencyFormat;

        var total = sheet.Range(totalRow, 1, totalRow, 3);
        total.Style.Font.Bold = true;
        total.Style.Border.TopBorder = XLBorderStyleValues.Thin;

        sheet.Columns().AdjustToContents();

        AddTopProductsSheet(workbook, topProducts);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // Second sheet: best sellers (units sold and revenue per product), most units first
    private static void AddTopProductsSheet(XLWorkbook workbook, IReadOnlyList<TopProductDto> topProducts)
    {
        var sheet = workbook.Worksheets.Add(TopSheetName);

        sheet.Cell(1, 1).Value = "STT";
        sheet.Cell(1, 2).Value = "Sản phẩm";
        sheet.Cell(1, 3).Value = "Số lượng đã bán";
        sheet.Cell(1, 4).Value = "Doanh thu";
        var header = sheet.Range(1, 1, 1, 4);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;
        sheet.SheetView.FreezeRows(1);

        var row = 2;
        foreach (var product in topProducts)
        {
            sheet.Cell(row, 1).Value = row - 1;
            sheet.Cell(row, 2).Value = product.Name;
            sheet.Cell(row, 3).Value = product.QuantitySold;
            sheet.Cell(row, 4).Value = product.Revenue;
            row++;
        }

        if (topProducts.Count > 0)
        {
            sheet.Range(2, 3, row - 1, 3).Style.NumberFormat.Format = "#,##0";
            sheet.Range(2, 4, row - 1, 4).Style.NumberFormat.Format = CurrencyFormat;
        }

        sheet.Columns().AdjustToContents();
    }
}
