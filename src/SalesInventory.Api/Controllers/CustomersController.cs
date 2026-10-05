using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;
using SalesInventory.Domain.Entities;
using SalesInventory.Infrastructure.Identity;

namespace SalesInventory.Api.Controllers;

// Customers: restricted to Admin and BanHang
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{AppRoles.Admin},{AppRoles.BanHang}")]
public class CustomersController : ControllerBase
{
    private const int MaxPageSize = 100;

    private readonly ICustomerService _customerService;
    private readonly ISalesOrderService _salesOrderService;
    private readonly IMapper _mapper;

    public CustomersController(ICustomerService customerService, ISalesOrderService salesOrderService, IMapper mapper)
    {
        _customerService = customerService;
        _salesOrderService = salesOrderService;
        _mapper = mapper;
    }

    /// <summary>Gets a page of customers.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerDto>>> GetCustomers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
        {
            ModelState.AddModelError(nameof(page), $"page must be >= 1 and pageSize must be between 1 and {MaxPageSize}.");
            return ValidationProblem(ModelState);
        }

        var (items, total) = await _customerService.GetCustomersAsync(page, pageSize);
        return Ok(new PagedResult<CustomerDto>
        {
            Items = _mapper.Map<List<CustomerDto>>(items),
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        });
    }

    /// <summary>Gets a single customer by id.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<CustomerDto>> GetCustomer(int id)
    {
        var customer = await _customerService.GetCustomerAsync(id);
        return customer is null ? NotFound() : Ok(_mapper.Map<CustomerDto>(customer));
    }

    /// <summary>Gets the sales order history of a customer, newest first.</summary>
    [HttpGet("{id}/orders")]
    public async Task<ActionResult<IEnumerable<OrderDto>>> GetCustomerOrders(int id)
    {
        if (await _customerService.GetCustomerAsync(id) is null)
        {
            return NotFound();
        }

        var orders = await _salesOrderService.GetOrdersByCustomerAsync(id);
        return Ok(_mapper.Map<List<OrderDto>>(orders));
    }

    /// <summary>Creates a new customer.</summary>
    [HttpPost]
    public async Task<ActionResult<CustomerDto>> CreateCustomer(CreateCustomerDto dto)
    {
        var created = await _customerService.CreateCustomerAsync(_mapper.Map<Customer>(dto));
        return CreatedAtAction(nameof(GetCustomer), new { id = created.Id }, _mapper.Map<CustomerDto>(created));
    }

    /// <summary>Updates an existing customer.</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCustomer(int id, UpdateCustomerDto dto)
    {
        var updated = await _customerService.UpdateCustomerAsync(id, _mapper.Map<Customer>(dto));
        return updated ? NoContent() : NotFound();
    }

    /// <summary>Deletes a customer that has no orders (409 otherwise).</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCustomer(int id)
    {
        var deleted = await _customerService.DeleteCustomerAsync(id);
        return deleted ? NoContent() : NotFound();
    }
}
