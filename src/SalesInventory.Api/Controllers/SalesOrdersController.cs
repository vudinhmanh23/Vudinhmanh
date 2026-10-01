using SalesInventory.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesInventory.Application.Dtos;
using SalesInventory.Domain.Entities;
using SalesInventory.Application.Interfaces;

namespace SalesInventory.Api.Controllers;

// Sales orders: Admin, BanHang and Kho may create/read; delete is Admin only
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{AppRoles.Admin},{AppRoles.BanHang},{AppRoles.Kho}")]
public class SalesOrdersController : ControllerBase
{
    private readonly ISalesOrderService _salesOrderService;
    private readonly IProductService _productService;

    public SalesOrdersController(ISalesOrderService salesOrderService, IProductService productService)
    {
        _salesOrderService = salesOrderService;
        _productService = productService;
    }

    /// <summary>Gets all sales orders.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrderDto>>> GetOrders()
    {
        var orders = await _salesOrderService.GetOrdersAsync();
        var items = await _salesOrderService.GetOrderItemsAsync();
        var itemsByOrderId = items.ToLookup(i => i.OrderId);
        var productNamesById = await GetProductNamesByIdAsync();

        return Ok(orders.Select(o => ToDto(o, itemsByOrderId[o.Id], productNamesById)));
    }

    /// <summary>Gets a single sales order by id.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<OrderDto>> GetOrder(int id)
    {
        var order = await _salesOrderService.GetOrderAsync(id);
        if (order is null)
        {
            return NotFound();
        }

        var items = await _salesOrderService.GetOrderItemsAsync();
        var productNamesById = await GetProductNamesByIdAsync();

        return Ok(ToDto(order, items.Where(i => i.OrderId == id), productNamesById));
    }

    /// <summary>Creates a new sales order with its line items.</summary>
    [HttpPost]
    public async Task<ActionResult<OrderDto>> CreateOrder(CreateOrderDto dto)
    {
        var order = new Order
        {
            OrderDate = dto.OrderDate,
            CustomerId = dto.CustomerId
        };

        var items = dto.Items.Select(i => new OrderItem
        {
            ProductId = i.ProductId,
            Quantity = i.Quantity,
            UnitPrice = i.UnitPrice
        });

        try
        {
            var created = await _salesOrderService.CreateOrderAsync(order, items);
            var createdItems = await _salesOrderService.GetOrderItemsAsync();
            var productNamesById = await GetProductNamesByIdAsync();

            return CreatedAtAction(
                nameof(GetOrder),
                new { id = created.Id },
                ToDto(created, createdItems.Where(i => i.OrderId == created.Id), productNamesById));
        }
        catch (ArgumentException ex)
        {
            // Business validation failure from the service layer
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Deletes a sales order and its line items.</summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> DeleteOrder(int id)
    {
        var deleted = await _salesOrderService.DeleteOrderAsync(id);
        return deleted ? NoContent() : NotFound();
    }

    private async Task<Dictionary<int, string>> GetProductNamesByIdAsync()
    {
        var products = await _productService.GetProductsAsync();
        return products.ToDictionary(p => p.Id, p => p.Name);
    }

    private static OrderDto ToDto(Order order, IEnumerable<OrderItem> items, Dictionary<int, string> productNamesById)
    {
        return new OrderDto
        {
            Id = order.Id,
            OrderDate = order.OrderDate,
            CustomerId = order.CustomerId,
            Items = items.Select(i => new OrderItemDto
            {
                ProductId = i.ProductId,
                ProductName = productNamesById.GetValueOrDefault(i.ProductId),
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };
    }
}
