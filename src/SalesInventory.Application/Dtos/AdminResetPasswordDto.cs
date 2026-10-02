using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Application.Dtos;

// Request body for POST /api/admin/users/{id}/reset-password
public class AdminResetPasswordDto
{
    [Required(ErrorMessage = "Mật khẩu mới là bắt buộc.")]
    public string NewPassword { get; set; } = string.Empty;
}
