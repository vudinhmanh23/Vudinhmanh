namespace SalesInventory.Application.Interfaces;

// Stores uploaded product images; the implementation decides where (disk today, blob storage later)
public interface IFileStorage
{
    // Saves the stream under a server-generated name (Guid + extension) and returns its public URL.
    // The extension must already be validated by the caller.
    Task<string> SaveProductImageAsync(Stream content, string extension, CancellationToken cancellationToken = default);

    // Deletes a file previously returned by SaveProductImageAsync; unknown or foreign URLs are ignored
    void DeleteProductImage(string? imageUrl);
}
