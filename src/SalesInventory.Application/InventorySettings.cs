namespace SalesInventory.Application;

// Bound from the "Inventory" configuration section
public class InventorySettings
{
    // A sale that leaves a product below this quantity adds a warning to the response
    public int LowStockThreshold { get; set; } = 5;
}
