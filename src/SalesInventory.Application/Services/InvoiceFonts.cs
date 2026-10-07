using QuestPDF.Drawing;

namespace SalesInventory.Application.Services;

// The default QuestPDF font lacks Vietnamese diacritics, so Noto Sans is embedded in this assembly and registered once
public static class InvoiceFonts
{
    public const string Family = "Noto Sans";

    private static readonly string[] Files = { "NotoSans-Regular.ttf", "NotoSans-Bold.ttf" };
    private static readonly object Gate = new();
    private static bool _registered;

    public static void Register()
    {
        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            var assembly = typeof(InvoiceFonts).Assembly;
            foreach (var file in Files)
            {
                using var stream = assembly.GetManifestResourceStream($"Fonts.{file}")
                    ?? throw new InvalidOperationException($"Embedded font '{file}' not found.");
                FontManager.RegisterFontFromStream(stream);
            }

            _registered = true;
        }
    }
}
