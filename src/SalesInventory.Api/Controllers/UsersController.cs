using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SalesInventory.Api.Dtos;
using SalesInventory.Api.Models;

namespace SalesInventory.Api.Controllers;

// Account/role management: Admin only
[ApiController]
[Route("api/users")]
[Authorize(Roles = "Admin")]
public class UsersController : ControllerBase
{
    private static readonly string[] AllowedRoles = { "Admin", "WarehouseManager", "SalesStaff" };

    private readonly UserManager<ApplicationUser> _userManager;

    public UsersController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    /// <summary>Gets all user accounts with their assigned roles.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers()
    {
        var users = await _userManager.Users.ToListAsync();
        var result = new List<UserDto>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            result.Add(new UserDto
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                Roles = roles.ToList()
            });
        }

        return Ok(result);
    }

    /// <summary>Assigns a role to a user account.</summary>
    [HttpPost("{id}/roles")]
    public async Task<IActionResult> AssignRole(string id, AssignRoleDto dto)
    {
        if (!AllowedRoles.Contains(dto.Role))
        {
            return BadRequest($"Role must be one of: {string.Join(", ", AllowedRoles)}");
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        if (await _userManager.IsInRoleAsync(user, dto.Role))
        {
            return Ok(new { message = "User already has this role." });
        }

        var result = await _userManager.AddToRoleAsync(user, dto.Role);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors.Select(e => e.Description));
        }

        return Ok(new { message = "Role assigned." });
    }

    /// <summary>Removes a role from a user account.</summary>
    [HttpDelete("{id}/roles/{role}")]
    public async Task<IActionResult> RemoveRole(string id, string role)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        var result = await _userManager.RemoveFromRoleAsync(user, role);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors.Select(e => e.Description));
        }

        return NoContent();
    }
}
