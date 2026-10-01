namespace SalesInventory.Infrastructure.Identity;

// Bound from the "Jwt" configuration section; Key is supplied via user-secrets, never committed
public class JwtSettings
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    // double so short lifetimes can be configured for testing (0.5 = 30 seconds)
    public double ExpiryMinutes { get; set; } = 60;
}
