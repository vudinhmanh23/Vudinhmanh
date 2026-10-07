using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SalesInventory.Application.Services;

// Shared page header of the invoice and the revenue report: logo, shop, school/student id, optional contact lines
public static class PdfHeader
{
    public static void Compose(IContainer container, ShopSettings shop, string title)
    {
        var logo = LoadLogo(shop.LogoPath);

        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                if (logo is not null)
                {
                    row.ConstantItem(60).Height(60).Image(logo).FitArea();
                    row.ConstantItem(12);
                }

                row.RelativeItem().Column(info =>
                {
                    info.Item().Text(shop.Name).FontSize(18).Bold();
                    AddLine(info, shop.SchoolName is { Length: > 0 } school ? $"Trường: {school}" : null);
                    AddLine(info, shop.StudentId is { Length: > 0 } id ? $"Mã số sinh viên: {id}" : null);
                    AddLine(info, shop.Address);
                    AddLine(info, shop.Phone is { Length: > 0 } phone ? $"Điện thoại: {phone}" : null);
                });
            });

            column.Item().PaddingTop(12).AlignCenter().Text(title).FontSize(16).Bold();
        });
    }

    private static void AddLine(ColumnDescriptor column, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            column.Item().Text(text);
        }
    }

    // A missing or unreadable logo must never break the document, so it is simply left out
    private static byte[]? LoadLogo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(path);
            return File.Exists(full) ? File.ReadAllBytes(full) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
