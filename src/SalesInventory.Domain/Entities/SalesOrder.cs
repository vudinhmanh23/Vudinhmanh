using System.ComponentModel.DataAnnotations;
using SalesInventory.Domain.Enums;

namespace SalesInventory.Domain.Entities;

// Represents a sales order placed by a customer
public class SalesOrder
{
    [Key]
    public int Id { get; set; }

    // Human-readable unique number, e.g. SO-20261005-0001; generated server-side
    [Required]
    [MaxLength(30)]
    public string OrderNumber { get; set; } = string.Empty;

    [Required]
    public DateTime OrderDate { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    // Flat discount subtracted from the sum of the line totals
    public decimal DiscountAmount { get; set; }

    // Sum of LineTotal minus DiscountAmount, never negative; always computed server-side
    public decimal TotalAmount { get; set; }

    // Optional free-text remark about the order
    [MaxLength(500)]
    public string? Note { get; set; }

    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Completed;

    // Line items belonging to this order
    public ICollection<SalesOrderItem> Items { get; set; } = new List<SalesOrderItem>();
}
