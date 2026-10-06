using SalesInventory.Application.Interfaces;

namespace SalesInventory.Infrastructure.Storage;

// Stores product images under <webRoot>/uploads/products and exposes them as "/uploads/products/<name>"
public class LocalFileStorage : IFileStorage
{
    private const string UrlPrefix = "/uploads/products/";

    private readonly string _productsDirectory;

    public LocalFileStorage(string webRootPath)
    {
        _productsDirectory = Path.GetFullPath(Path.Combine(webRootPath, "uploads", "products"));
    }

    public async Task<string> SaveProductImageAsync(Stream content, string extension, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_productsDirectory);

        // The name is generated here, never taken from the client
        var fileName = Guid.NewGuid().ToString("N") + extension;
        var path = Path.Combine(_productsDirectory, fileName);

        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(stream, cancellationToken);
        }

        return UrlPrefix + fileName;
    }

    public void DeleteProductImage(string? imageUrl)
    {
        if (imageUrl is null || !imageUrl.StartsWith(UrlPrefix, StringComparison.Ordinal))
        {
            return;
        }

        // Only a bare file name inside the uploads folder is ever deleted (no path traversal)
        var fileName = imageUrl[UrlPrefix.Length..];
        if (fileName.Length == 0 || fileName != Path.GetFileName(fileName))
        {
            return;
        }

        var path = Path.Combine(_productsDirectory, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
