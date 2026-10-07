using System.Globalization;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SalesInventory.Application.Interfaces;
using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Services;

public class InvoicePdfService : IInvoicePdfService
{
    private static readonly CultureInfo ViCulture = new("vi-VN");

    private readonly ShopSettings _shop;

    public InvoicePdfService(IOptions<ShopSettings> shop)
    {
        _shop = shop.Value;
        InvoiceFonts.Register();
    }

    public byte[] Generate(SalesOrder order) => BuildDocument(order).GeneratePdf();

    private IDocument BuildDocument(SalesOrder order)
    {
        // All amounts come from the stored order (LineTotal, DiscountAmount, TotalAmount), never recomputed here
        var document = Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontFamily(InvoiceFonts.Family).FontSize(10));

            page.Header().Element(header => PdfHeader.Compose(header, _shop, "HÓA ĐƠN BÁN HÀNG"));

            page.Content().PaddingVertical(15).Column(column =>
            {
                column.Spacing(4);
                column.Item().Text(text =>
                {
                    text.Span("Mã đơn: ").Bold();
                    text.Span(order.OrderNumber);
                });
                column.Item().Text(text =>
                {
                    text.Span("Ngày đặt: ").Bold();
                    text.Span(order.OrderDate.ToString("dd/MM/yyyy HH:mm", ViCulture));
                });
                column.Item().Text(text =>
                {
                    text.Span("Khách hàng: ").Bold();
                    text.Span(order.Customer?.Name ?? "Khách lẻ");
                });

                column.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(30);
                        c.RelativeColumn(4);
                        c.ConstantColumn(60);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderCell).Text("STT");
                        header.Cell().Element(HeaderCell).Text("Sản phẩm");
                        header.Cell().Element(HeaderCell).AlignRight().Text("Số lượng");
                        header.Cell().Element(HeaderCell).AlignRight().Text("Đơn giá");
                        header.Cell().Element(HeaderCell).AlignRight().Text("Thành tiền");
                    });

                    var index = 1;
                    foreach (var item in order.Items)
                    {
                        table.Cell().Element(BodyCell).Text(index++.ToString());
                        table.Cell().Element(BodyCell).Text(item.Product?.Name ?? $"Sản phẩm #{item.ProductId}");
                        table.Cell().Element(BodyCell).AlignRight().Text(item.Quantity.ToString("N0", ViCulture));
                        table.Cell().Element(BodyCell).AlignRight().Text(Vnd(item.UnitPrice));
                        table.Cell().Element(BodyCell).AlignRight().Text(Vnd(item.LineTotal));
                    }
                });

                column.Item().PaddingTop(10).AlignRight().Column(totals =>
                {
                    if (order.DiscountAmount > 0)
                    {
                        totals.Item().Text($"Giảm giá: {Vnd(order.DiscountAmount)}");
                    }

                    totals.Item().Text($"Tổng tiền: {Vnd(order.TotalAmount)}").FontSize(13).Bold();
                });

                if (!string.IsNullOrWhiteSpace(order.Note))
                {
                    column.Item().PaddingTop(10).Text($"Ghi chú: {order.Note}").Italic();
                }
            });

            page.Footer().AlignCenter().Text("Cảm ơn quý khách!").FontSize(9);
        }));

        return document;
    }

    // Vietnamese currency: dot as thousands separator and a trailing "đ", e.g. 1.250.000 đ
    private static string Vnd(decimal value) => $"{value.ToString("N0", ViCulture)} đ";

    private static IContainer HeaderCell(IContainer c) =>
        c.Background(Colors.Grey.Lighten3).BorderBottom(1).Padding(4).DefaultTextStyle(x => x.Bold());

    private static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4);
}
