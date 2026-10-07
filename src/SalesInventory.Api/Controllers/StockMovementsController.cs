using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;
using SalesInventory.Infrastructure.Identity;

namespace SalesInventory.Api.Controllers;

// Read-only view of the stock ledger; same access rule as GET /api/products/{id}/movements
[ApiController]
[Route("api/stock-movements")]
[Authorize(Policy = AuthPolicies.CanManageInventory)]
public class StockMovementsController : ControllerBase
{
    private readonly IStockMovementService _stockMovementService;
    private readonly IMapper _mapper;

    public StockMovementsController(IStockMovementService stockMovementService, IMapper mapper)
    {
        _stockMovementService = stockMovementService;
        _mapper = mapper;
    }

    /// <summary>Gets the stock movement history of one product, newest first. 404 when the product does not exist.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StockMovementDto>>> GetMovements([FromQuery] int productId)
    {
        // An absent query value binds to 0, which no validation attribute would catch, so check it explicitly
        if (productId <= 0)
        {
            ModelState.AddModelError(nameof(productId), "productId is required and must be a positive integer.");
            return ValidationProblem(ModelState);
        }

        var movements = await _stockMovementService.GetProductMovementsAsync(productId);
        if (movements is null)
        {
            return NotFound();
        }

        return Ok(_mapper.Map<IEnumerable<StockMovementDto>>(movements));
    }
}
