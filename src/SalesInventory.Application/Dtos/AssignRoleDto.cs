using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Application.Dtos;

public class AssignRoleDto
{
    [Required]
    public string Role { get; set; } = string.Empty;
}
