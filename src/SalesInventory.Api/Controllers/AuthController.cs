using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SalesInventory.Api.Dtos;
using SalesInventory.Api.Models;
using SalesInventory.Api.Services;

namespace SalesInventory.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private static readonly string[] AllowedRoles = { "Admin", "WarehouseManager", "SalesStaff" };
    private const string DefaultRole = "SalesStaff";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJwtTokenService _tokenService;

    public AuthController(UserManager<ApplicationUser> userManager, IJwtTokenService tokenService)
    {
        _userManager = userManager;
        _tokenService = tokenService;
    }

    /// <summary>Registers a new user account.</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        var role = string.IsNullOrWhiteSpace(dto.Role) ? DefaultRole : dto.Role;
        if (!AllowedRoles.Contains(role))
        {
            return BadRequest($"Role must be one of: {string.Join(", ", AllowedRoles)}");
        }

        var user = new ApplicationUser { UserName = dto.Email, Email = dto.Email };
        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors.Select(e => e.Description));
        }

        await _userManager.AddToRoleAsync(user, role);

        return Ok(new { message = "Registered successfully." });
    }

    /// <summary>Authenticates a user and returns a JWT.</summary>
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user is null || !await _userManager.CheckPasswordAsync(user, dto.Password))
        {
            return Unauthorized("Invalid email or password.");
        }

        var roles = await _userManager.GetRolesAsync(user);
        var token = _tokenService.GenerateToken(user, roles, out var expiresAtUtc);

        return Ok(new AuthResponseDto { Token = token, ExpiresAtUtc = expiresAtUtc });
    }

    /// <summary>Assigns a role to an existing account. Admin only.</summary>
    [HttpPost("assign-role")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AssignRole(AssignRoleRequestDto dto)
    {
        if (!AllowedRoles.Contains(dto.Role))
        {
            return BadRequest($"Role must be one of: {string.Join(", ", AllowedRoles)}");
        }

        var user = await _userManager.FindByEmailAsync(dto.Email);
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
}
