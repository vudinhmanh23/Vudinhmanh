using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Api.Dtos;

// Write model for creating a category
public class CreateCategoryDto
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }
}
