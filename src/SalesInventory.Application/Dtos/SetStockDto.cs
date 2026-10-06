using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Application.Dtos;

// Write model for a stocktake: the counted quantity replaces the recorded stock
public class SetStockDto
{
    // The real quantity on the shelf; the movement's Quantity is computed as this minus the recorded stock
    [Range(0, 1000000)]
    public int NewQuantity { get; set; }

    // Why the stock is being corrected; kept in the movement note for audit
    [Required(AllowEmptyStrings = false)]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}
