namespace SalesInventory.Infrastructure.Identity;

// Single source of truth for role names (const so they can be used in [Authorize(Roles = ...)])
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Kho = "Kho";
    public const string BanHang = "BanHang";

    public static readonly string[] All = { Admin, Kho, BanHang };
}
