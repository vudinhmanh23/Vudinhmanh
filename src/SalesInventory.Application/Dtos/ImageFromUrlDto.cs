using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Application.Dtos;

// Request body of the "set product image from a URL" endpoint
public class ImageFromUrlDto
{
    [Required]
    [MaxLength(2000)]
    public string Url { get; set; } = string.Empty;
}
