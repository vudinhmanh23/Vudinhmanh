using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SalesInventory.Application.Dtos;
using SalesInventory.Infrastructure.Identity;

namespace SalesInventory.Api.Controllers;

// Employee account creation: Admin only
[ApiController]
[Route("api/admin/employees")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public class AdminEmployeesController : ControllerBase
{
    // Employees are Kho or BanHang only; new Admins are not created through this endpoint
    private static readonly string[] EmployeeRoles = { AppRoles.Kho, AppRoles.BanHang };

    private readonly UserManager<ApplicationUser> _userManager;

    public AdminEmployeesController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    /// <summary>Creates an employee account and assigns it the "Kho" or "BanHang" role. Admin only.</summary>
    [HttpPost]
    public async Task<ActionResult<UserDto>> CreateEmployee(CreateEmployeeDto dto)
    {
        if (!EmployeeRoles.Contains(dto.Role))
        {
            return BadRequest(new { message = $"Role phải là một trong: {string.Join(", ", EmployeeRoles)}." });
        }

        if (await _userManager.FindByEmailAsync(dto.Email) is not null)
        {
            return Conflict(new { message = $"Email '{dto.Email}' đã được sử dụng." });
        }

        var user = new ApplicationUser { UserName = dto.Email, Email = dto.Email, FullName = dto.FullName };

        // Identity validates the password against the configured policy and hashes it
        var createResult = await _userManager.CreateAsync(user, dto.Password);
        if (!createResult.Succeeded)
        {
            // Covers the race where the same email is created between the check above and here
            if (createResult.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail"))
            {
                return Conflict(new { message = $"Email '{dto.Email}' đã được sử dụng." });
            }

            return BadRequest(new { message = "Không thể tạo tài khoản.", errors = createResult.Errors.Select(e => e.Description) });
        }

        var roleResult = await _userManager.AddToRoleAsync(user, dto.Role);
        if (!roleResult.Succeeded)
        {
            // Don't leave an account without a role behind
            await _userManager.DeleteAsync(user);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Không thể gán vai trò cho tài khoản." });
        }

        var result = new UserDto { Id = user.Id, Email = user.Email!, Roles = new List<string> { dto.Role } };
        return Created($"/api/users/{user.Id}", result);
    }
}
