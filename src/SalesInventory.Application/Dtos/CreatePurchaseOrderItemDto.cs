namespace SalesInventory.Application.Dtos;

// Write model for a purchase order line item (LineTotal is computed on the server, not accepted here)
public class CreatePurchaseOrderItemDto
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
