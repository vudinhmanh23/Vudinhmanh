using SalesInventory.Api.Models;
using SalesInventory.Domain.Entities;

namespace SalesInventory.Api.Services;

public interface IJwtTokenService
{
    string GenerateToken(ApplicationUser user, IList<string> roles, out DateTime expiresAtUtc);
}
