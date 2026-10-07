namespace SalesInventory.Application;

// Bound from the "Shop" configuration section; printed in the header of the invoice PDF
public class ShopSettings
{
    public string Name { get; set; } = "Cửa hàng SalesInventory";

    public string? Address { get; set; }

    public string? Phone { get; set; }
}
