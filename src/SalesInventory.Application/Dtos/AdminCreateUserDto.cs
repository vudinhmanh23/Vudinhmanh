using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Application.Dtos;

// Request body for POST /api/admin/users
public class AdminCreateUserDto
{
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    public string Email { get; set; } = string.Empty;

    // Temporary password; validated against the Identity password policy
    [Required(ErrorMessage = "Mật khẩu tạm là bắt buộc.")]
    public string TemporaryPassword { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Cần ít nhất một vai trò.")]
    [MinLength(1, ErrorMessage = "Cần ít nhất một vai trò.")]
    public List<string> Roles { get; set; } = new();
}
