namespace SalesInventory.Application.Interfaces;

// Finds a product picture on the internet from its barcode
public interface IProductImageLookup
{
    // Returns the image URL, or null when the barcode is unknown or has no picture.
    // Throws ImageDownloadException (upstream) when the lookup service cannot be reached.
    Task<Uri?> FindByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);
}
