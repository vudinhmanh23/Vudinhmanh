using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Application.Dtos;

// Write model for creating a supplier
public class CreateSupplierDto
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Phone { get; set; } = string.Empty;

    [EmailAddress]
    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }
}
