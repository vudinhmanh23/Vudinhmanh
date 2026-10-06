namespace SalesInventory.Application.Services;

// Rules every product image must satisfy, whether it was uploaded or downloaded from a URL
public static class ProductImageRules
{
    public const long MaxBytes = 2 * 1024 * 1024;

    // Enough leading bytes to recognise every supported signature
    public const int SignatureLength = 12;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.Ordinal) { ".jpg", ".jpeg", ".png", ".webp" };
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    public static bool IsAllowedExtension(string extension) => AllowedExtensions.Contains(extension);

    // Accepts "image/png" as well as "image/png; charset=binary"
    public static bool IsAllowedContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var separator = contentType.IndexOf(';');
        var mediaType = (separator >= 0 ? contentType[..separator] : contentType).Trim();
        return AllowedContentTypes.Contains(mediaType);
    }

    // Identifies the image type from the file's own leading bytes (".png", ".jpg" or ".webp"); null when it is none of them.
    // Extension and content type are client-declared and can be faked, the content cannot.
    public static string? DetectExtension(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return ".png";
        }

        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ".jpg";
        }

        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return ".webp";
        }

        return null;
    }

    // True when the content really is the type the (already whitelisted) extension claims
    public static bool ContentMatchesExtension(ReadOnlySpan<byte> header, string extension)
    {
        var detected = DetectExtension(header);
        var expected = extension == ".jpeg" ? ".jpg" : extension;
        return detected is not null && detected == expected;
    }
}
