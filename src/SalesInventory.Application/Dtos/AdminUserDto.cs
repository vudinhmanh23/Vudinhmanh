namespace SalesInventory.Application.Dtos;

// Read model for GET/POST /api/admin/users. Deliberately has no password hash or security stamp.
public class AdminUserDto
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public bool IsLockedOut { get; set; }
}
