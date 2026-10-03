namespace SalesInventory.Domain.Enums;

// Lifecycle of a purchase order; stock only changes when an order moves from Draft to Approved
public enum PurchaseOrderStatus
{
    Draft = 0,
    Approved = 1,
    Cancelled = 2
}
