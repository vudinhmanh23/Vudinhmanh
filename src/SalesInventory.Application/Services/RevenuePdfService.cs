using System.Globalization;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;

namespace SalesInventory.Application.Services;

public class RevenuePdfService : IRevenuePdfService
{
    private static readonly CultureInfo ViCulture = new("vi-VN");

    private readonly ShopSettings _shop;

    public RevenuePdfService(IOptions<ShopSettings> shop)
    {
        _shop = shop.Value;
        InvoiceFonts.Register();
    }

    public byte[] Generate(IReadOnlyList<RevenueByPeriodDto> days, DateTime from, DateTime to) =>
        BuildDocument(days, from, to).GeneratePdf();

    private IDocument BuildDocument(IReadOnlyList<RevenueByPeriodDto> days, DateTime from, DateTime to)
    {
        // The rows come from the same report service as the Excel export; only the totals are summed here
        var totalOrders = days.Sum(d => d.OrderCount);
        var totalRevenue = days.Sum(d => d.Revenue);

        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontFamily(InvoiceFonts.Family).FontSize(10));

            page.Header().Element(header => PdfHeader.Compose(header, _shop, "BÁO CÁO DOANH THU THEO NGÀY"));

            page.Content().PaddingVertical(15).Column(column =>
            {
                column.Spacing(4);
                column.Item().Text(text =>
                {
                    text.Span("Từ ngày: ").Bold();
                    text.Span(from.ToString("dd/MM/yyyy", ViCulture));
                    text.Span("   Đến ngày: ").Bold();
                    text.Span(to.ToString("dd/MM/yyyy", ViCulture));
                });

                column.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(2);
                        c.RelativeColumn(1);
                        c.RelativeColumn(2);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderCell).Text("Ngày");
                        header.Cell().Element(HeaderCell).AlignRight().Text("Số đơn");
                        header.Cell().Element(HeaderCell).AlignRight().Text("Doanh thu");
                    });

                    foreach (var day in days)
                    {
                        var date = DateTime.ParseExact(day.Period, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                        table.Cell().Element(BodyCell).Text(date.ToString("dd/MM/yyyy", ViCulture));
                        table.Cell().Element(BodyCell).AlignRight().Text(day.OrderCount.ToString("N0", ViCulture));
                        table.Cell().Element(BodyCell).AlignRight().Text(Vnd(day.Revenue));
                    }

                    // Total row, bold with a top border
                    table.Cell().Element(TotalCell).Text("Tổng cộng");
                    table.Cell().Element(TotalCell).AlignRight().Text(totalOrders.ToString("N0", ViCulture));
                    table.Cell().Element(TotalCell).AlignRight().Text(Vnd(totalRevenue));
                });

                if (days.Count == 0)
                {
                    column.Item().PaddingTop(10).Text("Không có dữ liệu doanh thu trong khoảng thời gian đã chọn").Italic();
                }
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span("Trang ").FontSize(9);
                text.CurrentPageNumber().FontSize(9);
                text.Span(" / ").FontSize(9);
                text.TotalPages().FontSize(9);
            });
        }));
    }

    // Vietnamese currency: dot as thousands separator and a trailing "đ", e.g. 1.250.000 đ
    private static string Vnd(decimal value) => $"{value.ToString("N0", ViCulture)} đ";

    private static IContainer HeaderCell(IContainer c) =>
        c.Background(Colors.Grey.Lighten3).BorderBottom(1).Padding(4).DefaultTextStyle(x => x.Bold());

    private static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4);

    private static IContainer TotalCell(IContainer c) =>
        c.BorderTop(1).Padding(4).DefaultTextStyle(x => x.Bold());
}
