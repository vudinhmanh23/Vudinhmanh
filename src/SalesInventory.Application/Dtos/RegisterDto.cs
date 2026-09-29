using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Application.Dtos;

public class RegisterDto
{
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
    [MinLength(6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Họ tên là bắt buộc.")]
    public string FullName { get; set; } = string.Empty;

    // Optional role: Admin, WarehouseManager or SalesStaff. Defaults to SalesStaff when omitted.
    public string? Role { get; set; }
}
