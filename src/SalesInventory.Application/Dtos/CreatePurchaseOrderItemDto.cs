using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Application.Dtos;

// Write model for a purchase order line item
public class CreatePurchaseOrderItemDto
{
    [Required]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    [Range(0, double.MaxValue)]
    public decimal UnitPrice { get; set; }
}
