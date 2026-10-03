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
    /// Creates a purchase order (stock-in). The server computes every line total and the order total,
    /// increases each product's stock and logs a stock movement, all in one transaction.
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

        try
        {
            var created = await _purchaseOrderService.CreatePurchaseOrderAsync(order);

            // Reload so the response includes product names for the line items
            var loaded = await _purchaseOrderService.GetPurchaseOrderAsync(created.Id);
            return CreatedAtAction(nameof(GetPurchaseOrder), new { id = created.Id }, _mapper.Map<PurchaseOrderDto>(loaded));
        }
        catch (NotFoundException ex)
        {
            // Unknown SupplierId / ProductId
            return Problem(title: "Referenced record not found", detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (BusinessRuleException ex)
        {
            return Problem(title: "Business rule violated", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Deletes a purchase order and its line items, and takes the received goods back out of stock.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePurchaseOrder(int id)
    {
        try
        {
            var deleted = await _purchaseOrderService.DeletePurchaseOrderAsync(id);
            return deleted ? NoContent() : NotFound();
        }
        catch (BusinessRuleException ex)
        {
            // e.g. part of the received stock was already sold, so it cannot be taken back
            return Problem(title: "Business rule violated", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
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
