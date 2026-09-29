namespace SalesInventory.Infrastructure.Identity;

public interface IJwtTokenService
{
    string GenerateToken(string userId, string? email, IList<string> roles, out DateTime expiresAtUtc);
}
