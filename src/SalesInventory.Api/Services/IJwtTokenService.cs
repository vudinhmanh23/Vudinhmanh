using SalesInventory.Api.Models;

namespace SalesInventory.Api.Services;

public interface IJwtTokenService
{
    string GenerateToken(ApplicationUser user, IList<string> roles, out DateTime expiresAtUtc);
}
