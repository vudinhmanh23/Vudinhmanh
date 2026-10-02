using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SalesInventory.Application.Dtos;
using SalesInventory.Infrastructure.Identity;

namespace SalesInventory.Api.Controllers;

// Staff account management: Admin only
[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = AppRoles.Admin)]
public class UserAdminController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public UserAdminController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    /// <summary>Lists all users with their roles and lockout state.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AdminUserDto>>> GetUsers()
    {
        var users = await _userManager.Users.OrderBy(u => u.Email).ToListAsync();
        var result = new List<AdminUserDto>(users.Count);

        foreach (var user in users)
        {
            result.Add(await ToDtoAsync(user));
        }

        return Ok(result);
    }

    /// <summary>Gets one user's details (username, email, roles, lockout state).</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<AdminUserDto>> GetUser(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        return Ok(await ToDtoAsync(user));
    }

    /// <summary>Creates a staff account with a temporary password and initial roles.</summary>
    [HttpPost]
    public async Task<ActionResult<AdminUserDto>> CreateUser(AdminCreateUserDto dto)
    {
        var roles = dto.Roles.Distinct().ToList();
        var invalidRole = await FindUnknownRoleAsync(roles);
        if (invalidRole is not null)
        {
            return BadRequest(new { message = $"Vai trò '{invalidRole}' không tồn tại." });
        }

        if (await _userManager.FindByEmailAsync(dto.Email) is not null)
        {
            return Conflict(new { message = $"Email '{dto.Email}' đã được sử dụng." });
        }

        var user = new ApplicationUser { UserName = dto.Email, Email = dto.Email, FullName = dto.FullName };

        // Identity validates the password against the configured policy and hashes it
        var createResult = await _userManager.CreateAsync(user, dto.TemporaryPassword);
        if (!createResult.Succeeded)
        {
            // Covers the race where the same email is created between the check above and here
            if (createResult.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail"))
            {
                return Conflict(new { message = $"Email '{dto.Email}' đã được sử dụng." });
            }

            return BadRequest(new { message = "Không thể tạo tài khoản.", errors = createResult.Errors.Select(e => e.Description) });
        }

        var roleResult = await _userManager.AddToRolesAsync(user, roles);
        if (!roleResult.Succeeded)
        {
            // Don't leave an account without its roles behind
            await _userManager.DeleteAsync(user);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Không thể gán vai trò cho tài khoản." });
        }

        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, await ToDtoAsync(user));
    }

    /// <summary>Replaces the user's roles with the given list.</summary>
    [HttpPut("{id}/roles")]
    public async Task<ActionResult<AdminUserDto>> SetRoles(string id, SetUserRolesDto dto)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        var roles = dto.Roles.Distinct().ToList();
        var invalidRole = await FindUnknownRoleAsync(roles);
        if (invalidRole is not null)
        {
            return BadRequest(new { message = $"Vai trò '{invalidRole}' không tồn tại." });
        }

        // An admin must not strip their own Admin role (they could lock themselves out of this API)
        if (IsCurrentUser(user) && !roles.Contains(AppRoles.Admin))
        {
            return BadRequest(new { message = "Không thể tự gỡ vai trò Admin của chính mình." });
        }

        var current = await _userManager.GetRolesAsync(user);

        var removeResult = await _userManager.RemoveFromRolesAsync(user, current.Except(roles));
        if (!removeResult.Succeeded)
        {
            return BadRequest(new { message = "Không thể cập nhật vai trò.", errors = removeResult.Errors.Select(e => e.Description) });
        }

        var addResult = await _userManager.AddToRolesAsync(user, roles.Except(current));
        if (!addResult.Succeeded)
        {
            return BadRequest(new { message = "Không thể cập nhật vai trò.", errors = addResult.Errors.Select(e => e.Description) });
        }

        return Ok(await ToDtoAsync(user));
    }

    /// <summary>Locks the account until the end of time (login is refused until unlocked).</summary>
    [HttpPost("{id}/lock")]
    public async Task<ActionResult<AdminUserDto>> Lock(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        if (IsCurrentUser(user))
        {
            return BadRequest(new { message = "Không thể tự khóa tài khoản của chính mình." });
        }

        // Lockout must be enabled on the account for LockoutEnd to be honored
        var result = await _userManager.SetLockoutEnabledAsync(user, true);
        if (result.Succeeded)
        {
            result = await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        }

        if (!result.Succeeded)
        {
            return BadRequest(new { message = "Không thể khóa tài khoản.", errors = result.Errors.Select(e => e.Description) });
        }

        return Ok(await ToDtoAsync(user));
    }

    /// <summary>Unlocks the account and clears its failed-login counter.</summary>
    [HttpPost("{id}/unlock")]
    public async Task<ActionResult<AdminUserDto>> Unlock(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        var result = await _userManager.SetLockoutEndDateAsync(user, null);
        if (result.Succeeded)
        {
            result = await _userManager.ResetAccessFailedCountAsync(user);
        }

        if (!result.Succeeded)
        {
            return BadRequest(new { message = "Không thể mở khóa tài khoản.", errors = result.Errors.Select(e => e.Description) });
        }

        return Ok(await ToDtoAsync(user));
    }

    /// <summary>Sets a new password for the user (the value is never echoed back).</summary>
    [HttpPost("{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, AdminResetPasswordDto dto)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, dto.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(new { message = "Không thể đặt lại mật khẩu.", errors = result.Errors.Select(e => e.Description) });
        }

        return Ok(new { message = "Đã đặt lại mật khẩu." });
    }

    private bool IsCurrentUser(ApplicationUser user) =>
        user.Id == User.FindFirstValue(ClaimTypes.NameIdentifier);

    // Returns the first role name that does not exist, or null when all do
    private async Task<string?> FindUnknownRoleAsync(IEnumerable<string> roles)
    {
        foreach (var role in roles)
        {
            if (!await _roleManager.RoleExistsAsync(role))
            {
                return role;
            }
        }

        return null;
    }

    private async Task<AdminUserDto> ToDtoAsync(ApplicationUser user) => new()
    {
        Id = user.Id,
        UserName = user.UserName ?? string.Empty,
        Email = user.Email ?? string.Empty,
        FullName = user.FullName,
        Roles = (await _userManager.GetRolesAsync(user)).ToList(),
        IsLockedOut = await _userManager.IsLockedOutAsync(user)
    };
}
