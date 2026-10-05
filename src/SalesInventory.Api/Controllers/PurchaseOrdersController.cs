using AutoMapper;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesInventory.Infrastructure.Identity;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Exceptions;
using SalesInventory.Domain.Entities;
using SalesInventory.Application.Interfaces;

namespace SalesInventory.Api.Controllers;

// Purchase orders (stock-in): whole controller requires the "CanManageInventory" policy (Admin or Kho)
[ApiController]
[Route("api/purchase-orders")]
[Authorize(Policy = AuthPolicies.CanManageInventory)]
public class PurchaseOrdersController : ControllerBase
{
    private readonly IPurchaseOrderService _purchaseOrderService;
    private readonly IMapper _mapper;
    private readonly IValidator<CreatePurchaseOrderDto> _createValidator;

    public PurchaseOrdersController(
        IPurchaseOrderService purchaseOrderService,
        IMapper mapper,
        IValidator<CreatePurchaseOrderDto> createValidator)
    {
        _purchaseOrderService = purchaseOrderService;
        _mapper = mapper;
        _createValidator = createValidator;
    }

    /// <summary>Gets all purchase orders, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PurchaseOrderDto>>> GetPurchaseOrders()
    {
        var orders = await _purchaseOrderService.GetPurchaseOrdersAsync();
        return Ok(_mapper.Map<IEnumerable<PurchaseOrderDto>>(orders));
    }

    /// <summary>Gets a single purchase order, with its line items, by id.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<PurchaseOrderDto>> GetPurchaseOrder(int id)
    {
        var order = await _purchaseOrderService.GetPurchaseOrderAsync(id);
        if (order is null)
        {
            return NotFound();
        }

        return Ok(_mapper.Map<PurchaseOrderDto>(order));
    }

    /// <summary>
    /// Creates a purchase order as a Draft. The server computes every line total and the order total.
    /// Stock is not changed until the order is approved (POST /api/purchase-orders/{id}/approve).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<PurchaseOrderDto>> CreatePurchaseOrder(CreatePurchaseOrderDto dto)
    {
        var validation = await _createValidator.ValidateAsync(dto);
        if (!validation.IsValid)
        {
            return ValidationFailure(validation);
        }

        var order = _mapper.Map<PurchaseOrder>(dto);

        var created = await _purchaseOrderService.CreatePurchaseOrderAsync(order);

        // Reload so the response includes product names for the line items
        var loaded = await _purchaseOrderService.GetPurchaseOrderAsync(created.Id);
        return CreatedAtAction(nameof(GetPurchaseOrder), new { id = created.Id }, _mapper.Map<PurchaseOrderDto>(loaded));
    }

    /// <summary>
    /// Approves a Draft purchase order: adds every line's quantity to the product's stock and logs one Purchase
    /// stock movement per line, all in one transaction. Returns 409 if the order is not a Draft.
    /// </summary>
    /// <response code="200">The approved order.</response>
    /// <response code="404">No purchase order with this id.</response>
    /// <response code="409">The order is not in the Draft state.</response>
    [HttpPost("{id}/approve")]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PurchaseOrderDto>> ApprovePurchaseOrder(int id)
    {
        var approved = await _purchaseOrderService.ApprovePurchaseOrderAsync(id);
        return Ok(_mapper.Map<PurchaseOrderDto>(approved));
    }

    /// <summary>
    /// Cancels an Approved purchase order: takes each line's quantity back out of stock and logs one negative
    /// Adjustment movement per line, all in one transaction. Returns 409 if the order is not Approved.
    /// </summary>
    /// <response code="200">The cancelled order.</response>
    /// <response code="404">No purchase order with this id.</response>
    /// <response code="409">The order is not Approved, or stock is too low to take the goods back.</response>
    [HttpPost("{id}/cancel")]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PurchaseOrderDto>> CancelPurchaseOrder(int id)
    {
        var cancelled = await _purchaseOrderService.CancelPurchaseOrderAsync(id);
        return Ok(_mapper.Map<PurchaseOrderDto>(cancelled));
    }

    /// <summary>Deletes a purchase order and its line items; an Approved order also has its stock taken back.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePurchaseOrder(int id)
    {
        var deleted = await _purchaseOrderService.DeletePurchaseOrderAsync(id);
        return deleted ? NoContent() : NotFound();
    }

    // Converts FluentValidation failures into a standard 400 ValidationProblemDetails response
    private ActionResult ValidationFailure(FluentValidation.Results.ValidationResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
        }

        return ValidationProblem(ModelState);
    }
}
