using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Application.Dtos;

// Write model for creating a sales order with its line items
public class CreateOrderDto
{
    [Required]
    public DateTime OrderDate { get; set; }

    [Required]
    public int CustomerId { get; set; }

    [Required]
    [MinLength(1)]
    public List<CreateOrderItemDto> Items { get; set; } = new();
}
