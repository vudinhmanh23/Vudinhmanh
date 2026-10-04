namespace SalesInventory.Application.Services;

// Raised when a product would end up with a barcode that another product already uses
public class DuplicateBarcodeException : Exception
{
    public DuplicateBarcodeException(string barcode) : base($"Barcode '{barcode}' đã tồn tại.")
    {
    }
}
