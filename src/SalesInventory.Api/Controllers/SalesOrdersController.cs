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
[Route("api/[controller]")]
[Authorize(Roles = $"{AppRoles.Admin},{AppRoles.BanHang},{AppRoles.Kho}")]
public class SalesOrdersController : ControllerBase
{
    private readonly ISalesOrderService _salesOrderService;
    private readonly IMapper _mapper;

    public SalesOrdersController(ISalesOrderService salesOrderService, IMapper mapper)
    {
        _salesOrderService = salesOrderService;
        _mapper = mapper;
    }

    /// <summary>Gets all sales orders.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrderDto>>> GetOrders()
    {
        var orders = await _salesOrderService.GetOrdersAsync();
        return Ok(_mapper.Map<List<OrderDto>>(orders));
    }

    /// <summary>Gets a single sales order by id.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<OrderDto>> GetOrder(int id)
    {
        var order = await _salesOrderService.GetOrderAsync(id);
        return order is null ? NotFound() : Ok(_mapper.Map<OrderDto>(order));
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
