using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Application.Dtos;

// Request body for POST /api/admin/employees
public class CreateEmployeeDto
{
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Họ tên là bắt buộc.")]
    public string FullName { get; set; } = string.Empty;

    // "Kho" or "BanHang"
    [Required(ErrorMessage = "Vai trò là bắt buộc.")]
    public string Role { get; set; } = string.Empty;
}
