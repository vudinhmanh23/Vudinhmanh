using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Application.Dtos;

// Write model for assigning a role to an existing account, identified by email
public class AssignRoleRequestDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = string.Empty;
}
