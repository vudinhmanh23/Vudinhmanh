using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Application.Dtos;

// Write model for updating an existing category
public class UpdateCategoryDto
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }
}
