using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Application.Dtos;

// Write model for a manual stock correction (e.g. after a stocktake finds a discrepancy)
public class AdjustStockDto
{
    // Signed change: positive adds stock, negative removes it; zero is meaningless and rejected
    [Range(-1000000, 1000000)]
    public int Delta { get; set; }

    // Why the stock is being corrected; kept in the movement note for audit
    [Required(AllowEmptyStrings = false)]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}
