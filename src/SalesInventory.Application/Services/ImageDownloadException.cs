namespace SalesInventory.Application.Services;

// Raised when an image cannot be fetched from a remote URL; the message is safe to show to the user
public class ImageDownloadException : Exception
{
    public ImageDownloadException(string message, bool isUpstreamError = false, Exception? inner = null)
        : base(message, inner)
    {
        IsUpstreamError = isUpstreamError;
    }

    // True when the remote server was unreachable or misbehaving, false when the request itself was unacceptable
    public bool IsUpstreamError { get; }
}
