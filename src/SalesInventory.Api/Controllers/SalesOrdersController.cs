using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;
using SalesInventory.Domain.Entities;
using SalesInventory.Infrastructure.Identity;

namespace SalesInventory.Api.Controllers;

// Sales orders: Admin, BanHang and Kho may create/read; delete is Admin only
[ApiController]
[Route("api/sales-orders")]
[Authorize(Roles = $"{AppRoles.Admin},{AppRoles.BanHang},{AppRoles.Kho}")]
public class SalesOrdersController : ControllerBase
{
    private readonly ISalesOrderService _salesOrderService;
    private readonly IMapper _mapper;
    private readonly IInvoicePdfService _invoicePdfService;

    public SalesOrdersController(ISalesOrderService salesOrderService, IMapper mapper, IInvoicePdfService invoicePdfService)
    {
        _salesOrderService = salesOrderService;
        _invoicePdfService = invoicePdfService;
        _mapper = mapper;
    }

    /// <summary>
    /// Gets sales orders, newest first. Without page/pageSize every order is returned; with them (pageSize 1-100)
    /// only that page, and the X-Total-Count header carries the total number of orders.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrderDto>>> GetOrders([FromQuery] int? page, [FromQuery] int? pageSize)
    {
        if (!PagingIsValid(page, pageSize, out var error))
        {
            ModelState.AddModelError(nameof(page), error);
            return ValidationProblem(ModelState);
        }

        if (page is not null)
        {
            Response.Headers["X-Total-Count"] = (await _salesOrderService.CountOrdersAsync()).ToString();
        }

        var orders = await _salesOrderService.GetOrdersAsync(page, pageSize);
        return Ok(_mapper.Map<List<OrderDto>>(orders));
    }

    // page and pageSize come together (both or neither) and stay within the allowed range
    private static bool PagingIsValid(int? page, int? pageSize, out string error)
    {
        error = string.Empty;
        if (page is null && pageSize is null)
        {
            return true;
        }

        if (page is null || pageSize is null || page < 1 || pageSize < 1 || pageSize > 100)
        {
            error = "page and pageSize must be given together: page >= 1 and pageSize between 1 and 100.";
            return false;
        }

        return true;
    }

    /// <summary>Gets a single sales order by id.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<OrderDto>> GetOrder(int id)
    {
        var order = await _salesOrderService.GetOrderAsync(id);
        return order is null ? NotFound() : Ok(_mapper.Map<OrderDto>(order));
    }

    /// <summary>Downloads the invoice of an order as a PDF file (invoice-{id}.pdf).</summary>
    [HttpGet("{id}/invoice-pdf")]
    [Produces("application/pdf")]
    public async Task<IActionResult> GetInvoicePdf(int id)
    {
        var order = await _salesOrderService.GetOrderAsync(id);
        if (order is null)
        {
            return NotFound();
        }

        return File(_invoicePdfService.Generate(order), "application/pdf", $"invoice-{id}.pdf");
    }

    /// <summary>
    /// Creates a sales order: deducts stock, logs one Sale movement per line, all in one transaction.
    /// Returns 409 when stock is insufficient; low-stock notices come back in <c>warnings</c>.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<OrderDto>> CreateOrder(CreateOrderDto dto)
    {
        var result = await _salesOrderService.CreateOrderAsync(_mapper.Map<SalesOrder>(dto));

        // Re-read so the response carries product names
        var created = await _salesOrderService.GetOrderAsync(result.Order.Id);
        var response = _mapper.Map<OrderDto>(created);
        response.Warnings = result.Warnings.ToList();
        response.LowStockProducts = result.LowStockProducts.ToList();

        return CreatedAtAction(nameof(GetOrder), new { id = response.Id }, response);
    }

    /// <summary>Deletes a sales order and its line items (stock is not restored).</summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> DeleteOrder(int id)
    {
        var deleted = await _salesOrderService.DeleteOrderAsync(id);
        return deleted ? NoContent() : NotFound();
    }
}
