namespace SalesInventory.Infrastructure.Identity;

// Names of the authorization policies registered in Program.cs
public static class AuthPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string CanManageInventory = "CanManageInventory";
    public const string SalesAccess = "SalesAccess";
}
