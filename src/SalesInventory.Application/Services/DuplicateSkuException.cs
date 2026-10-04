namespace SalesInventory.Application.Services;

// Raised when a product would end up with a SKU that another product already uses
public class DuplicateSkuException : Exception
{
    public DuplicateSkuException(string sku) : base($"SKU '{sku}' đã tồn tại.")
    {
    }
}
