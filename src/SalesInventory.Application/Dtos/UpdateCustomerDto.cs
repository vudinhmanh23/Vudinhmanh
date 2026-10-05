using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Application.Dtos;

// Write model for updating a customer
public class UpdateCustomerDto
{
    [Required(AllowEmptyStrings = false)]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(200)]
    [EmailAddress]
    public string? Email { get; set; }

    [MaxLength(400)]
    public string? Address { get; set; }
}
