using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SalesInventory.Application.Dtos;
using SalesInventory.Infrastructure.Identity;
using SalesInventory.Domain.Entities;

namespace SalesInventory.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private static readonly string[] AllowedRoles = AppRoles.All;
    private const string DefaultRole = AppRoles.BanHang;

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;

    public AuthController(UserManager<ApplicationUser> userManager, ITokenService tokenService)
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

        var user = new ApplicationUser { UserName = dto.Email, Email = dto.Email, FullName = dto.FullName };
        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new { message = DescribeRegistrationError(result.Errors) });
        }

        await _userManager.AddToRoleAsync(user, role);

        return Ok(new { message = "Đăng ký thành công" });
    }

    /// <summary>Authenticates a user and returns a JWT.</summary>
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user is null || !await _userManager.CheckPasswordAsync(user, dto.Password))
        {
            return Unauthorized(new { message = "Email hoặc mật khẩu không đúng" });
        }

        var roles = await _userManager.GetRolesAsync(user);
        var token = _tokenService.GenerateToken(user.Id, user.Email, roles, out var expiresAtUtc);

        return Ok(new AuthResponseDto { Token = token, ExpiresAt = expiresAtUtc });
    }

    /// <summary>Returns the current user's identity, read from the token claims (no database query).</summary>
    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        // The JWT handler maps "sub"/"email" to ClaimTypes.* by default; fall back to the raw names in case that is disabled
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(JwtRegisteredClaimNames.Email);
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        return Ok(new { id, email, roles });
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

    // Maps Identity's technical error codes to a single friendly Vietnamese message,
    // without leaking internal details (e.g. exact password rule wording).
    private static string DescribeRegistrationError(IEnumerable<IdentityError> errors)
    {
        var codes = errors.Select(e => e.Code).ToHashSet();

        if (codes.Contains("DuplicateUserName") || codes.Contains("DuplicateEmail"))
        {
            return "Email này đã được sử dụng. Vui lòng chọn email khác.";
        }

        if (codes.Contains("InvalidEmail"))
        {
            return "Email không hợp lệ.";
        }

        string[] passwordCodes =
        {
            "PasswordTooShort", "PasswordRequiresUpper", "PasswordRequiresLower",
            "PasswordRequiresDigit", "PasswordRequiresNonAlphanumeric", "PasswordRequiresUniqueChars"
        };
        if (codes.Overlaps(passwordCodes))
        {
            return "Mật khẩu chưa đủ mạnh. Mật khẩu cần tối thiểu 8 ký tự, có ít nhất một chữ hoa và một chữ số.";
        }

        return "Đăng ký không thành công. Vui lòng thử lại.";
    }
}
