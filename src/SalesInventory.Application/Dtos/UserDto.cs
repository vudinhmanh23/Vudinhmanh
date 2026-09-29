namespace SalesInventory.Application.Dtos;

// Read model returned by account/role management endpoints
public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
}
