using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Application.Dtos;

// Write model for creating a sales order with its line items
public class CreateOrderDto
{
    [Required]
    public DateTime OrderDate { get; set; }

    [Required]
    public int CustomerId { get; set; }

    // Flat discount off the order; the total (lines - discount) may not go below zero
    [Range(0, 999999999999.99)]
    public decimal DiscountAmount { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    // Model binding rejects an empty list with this message (400) before the service, let alone a transaction, is reached
    [Required(ErrorMessage = "Đơn hàng phải có ít nhất một mặt hàng")]
    [MinLength(1, ErrorMessage = "Đơn hàng phải có ít nhất một mặt hàng")]
    public List<CreateOrderItemDto> Items { get; set; } = new();
}
