using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Application.Dtos;

// Write model for creating a purchase order (stock-in) with its line items
public class CreatePurchaseOrderDto
{
    [Required]
    public DateTime OrderDate { get; set; }

    [Required]
    public int SupplierId { get; set; }

    [Required]
    [MinLength(1)]
    public List<CreatePurchaseOrderItemDto> Items { get; set; } = new();
}
