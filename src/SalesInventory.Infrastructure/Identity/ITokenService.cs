namespace SalesInventory.Infrastructure.Identity;

public interface ITokenService
{
    string GenerateToken(string userId, string? email, IList<string> roles, out DateTime expiresAtUtc);
}
