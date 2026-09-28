using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Api.Dtos;

public class RegisterDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    // Optional role: Admin, WarehouseManager or SalesStaff. Defaults to SalesStaff when omitted.
    public string? Role { get; set; }
}
