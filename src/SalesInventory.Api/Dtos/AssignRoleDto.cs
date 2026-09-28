using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Api.Dtos;

public class AssignRoleDto
{
    [Required]
    public string Role { get; set; } = string.Empty;
}
