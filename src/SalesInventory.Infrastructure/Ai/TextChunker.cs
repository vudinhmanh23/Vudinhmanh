namespace SalesInventory.Infrastructure.Ai;

// Cuts a document into passages of about chunkSize characters. A cut prefers a paragraph, line or sentence end in the
// second half of the window, and the next chunk starts `overlap` characters earlier so a sentence on the boundary is not lost.
public static class TextChunker
{
    public static IReadOnlyList<string> Split(string text, int chunkSize = 500, int overlap = 100)
    {
        if (chunkSize < 50)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be at least 50.");
        }

        overlap = Math.Clamp(overlap, 0, chunkSize / 2);
        text = text.Replace("\r\n", "\n").Trim();

        var chunks = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var end = Math.Min(start + chunkSize, text.Length);
            if (end < text.Length)
            {
                end = FindCut(text, start, end);
            }

            var chunk = text[start..end].Trim();
            if (chunk.Length > 0)
            {
                chunks.Add(chunk);
            }

            if (end >= text.Length)
            {
                break;
            }

            // Always move forward, even with a large overlap
            start = Math.Max(end - overlap, start + 1);
        }

        return chunks;
    }

    private static int FindCut(string text, int start, int end)
    {
        var floor = start + (end - start) / 2;
        foreach (var separator in new[] { "\n\n", "\n", ". ", "; ", ", ", " " })
        {
            var index = text.LastIndexOf(separator, end - 1, end - floor, StringComparison.Ordinal);
            if (index >= floor)
            {
                return index + separator.Length;
            }
        }

        return end;
    }
}
