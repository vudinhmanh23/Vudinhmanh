using Microsoft.AspNetCore.Identity;

namespace SalesInventory.Api.Models;

// Identity user for authentication; roles are managed separately via IdentityRole
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
}
