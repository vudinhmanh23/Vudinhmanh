using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SalesInventory.Api.Data;

namespace SalesInventory.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly AppDbContext _context;

    public HealthController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { status = "ok" });
    }

    [HttpGet("db")]
    public async Task<IActionResult> GetDb()
    {
        var canConnect = await _context.Database.CanConnectAsync();
        return canConnect
            ? Ok(new { status = "ok", database = "connected" })
            : StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "error", database = "unreachable" });
    }
}
