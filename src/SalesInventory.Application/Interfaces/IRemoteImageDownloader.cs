namespace SalesInventory.Application.Interfaces;

public record DownloadedImage(byte[] Content, string ContentType);

// Fetches an image from a user-supplied URL. Implementations must be SSRF-safe: the server may never be
// tricked into reaching internal addresses. Throws ImageDownloadException for anything unacceptable.
public interface IRemoteImageDownloader
{
    Task<DownloadedImage> DownloadAsync(Uri url, long maxBytes, CancellationToken cancellationToken = default);
}
