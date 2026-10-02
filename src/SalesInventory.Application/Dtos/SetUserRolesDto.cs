using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Application.Dtos;

// Request body for PUT /api/admin/users/{id}/roles: the complete new role list
public class SetUserRolesDto
{
    [Required(ErrorMessage = "Danh sách vai trò là bắt buộc.")]
    [MinLength(1, ErrorMessage = "Cần ít nhất một vai trò.")]
    public List<string> Roles { get; set; } = new();
}
