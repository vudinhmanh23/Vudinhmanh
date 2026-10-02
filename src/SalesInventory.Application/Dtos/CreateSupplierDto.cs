using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Application.Dtos;

// Write model for creating a supplier
public class CreateSupplierDto
{
    // Code, Name and Email are validated by FluentValidation (see Validators)
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? ContactPerson { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    public string? Email { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }
}
