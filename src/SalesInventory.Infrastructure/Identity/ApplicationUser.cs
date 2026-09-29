using Microsoft.AspNetCore.Identity;

namespace SalesInventory.Infrastructure.Identity;

// Identity user for authentication; roles are managed separately via IdentityRole
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
}
