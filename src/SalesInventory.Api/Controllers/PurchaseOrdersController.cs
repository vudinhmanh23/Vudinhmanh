using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesInventory.Infrastructure.Identity;
using SalesInventory.Application.Dtos;
using SalesInventory.Domain.Entities;
using SalesInventory.Application.Interfaces;

namespace SalesInventory.Api.Controllers;

// Purchase orders (stock-in): whole controller requires the "CanManageInventory" policy (Admin or Kho)
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthPolicies.CanManageInventory)]
public class PurchaseOrdersController : ControllerBase
{
    private readonly IPurchaseOrderService _purchaseOrderService;
    private readonly IProductService _productService;

    public PurchaseOrdersController(IPurchaseOrderService purchaseOrderService, IProductService productService)
    {
        _purchaseOrderService = purchaseOrderService;
        _productService = productService;
    }

    /// <summary>Gets all purchase orders.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PurchaseOrderDto>>> GetPurchaseOrders()
    {
        var orders = await _purchaseOrderService.GetPurchaseOrdersAsync();
        var items = await _purchaseOrderService.GetPurchaseOrderItemsAsync();
        var itemsByOrderId = items.ToLookup(i => i.PurchaseOrderId);
        var productNamesById = await GetProductNamesByIdAsync();

        return Ok(orders.Select(o => ToDto(o, itemsByOrderId[o.Id], productNamesById)));
    }

    /// <summary>Gets a single purchase order by id.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<PurchaseOrderDto>> GetPurchaseOrder(int id)
    {
        var order = await _purchaseOrderService.GetPurchaseOrderAsync(id);
        if (order is null)
        {
            return NotFound();
        }

        var items = await _purchaseOrderService.GetPurchaseOrderItemsAsync();
        var productNamesById = await GetProductNamesByIdAsync();

        return Ok(ToDto(order, items.Where(i => i.PurchaseOrderId == id), productNamesById));
    }

    /// <summary>Creates a new purchase order (stock-in) with its line items.</summary>
    [HttpPost]
    public async Task<ActionResult<PurchaseOrderDto>> CreatePurchaseOrder(CreatePurchaseOrderDto dto)
    {
        var order = new PurchaseOrder
        {
            OrderDate = dto.OrderDate,
            SupplierId = dto.SupplierId
        };

        var items = dto.Items.Select(i => new PurchaseOrderItem
        {
            ProductId = i.ProductId,
            Quantity = i.Quantity,
            UnitPrice = i.UnitPrice
        });

        try
        {
            var created = await _purchaseOrderService.CreatePurchaseOrderAsync(order, items);
            var createdItems = await _purchaseOrderService.GetPurchaseOrderItemsAsync();
            var productNamesById = await GetProductNamesByIdAsync();

            return CreatedAtAction(
                nameof(GetPurchaseOrder),
                new { id = created.Id },
                ToDto(created, createdItems.Where(i => i.PurchaseOrderId == created.Id), productNamesById));
        }
        catch (ArgumentException ex)
        {
            // Business validation failure from the service layer
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Deletes a purchase order and its line items.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePurchaseOrder(int id)
    {
        var deleted = await _purchaseOrderService.DeletePurchaseOrderAsync(id);
        return deleted ? NoContent() : NotFound();
    }

    private async Task<Dictionary<int, string>> GetProductNamesByIdAsync()
    {
        var products = await _productService.GetProductsAsync();
        return products.ToDictionary(p => p.Id, p => p.Name);
    }

    private static PurchaseOrderDto ToDto(PurchaseOrder order, IEnumerable<PurchaseOrderItem> items, Dictionary<int, string> productNamesById)
    {
        return new PurchaseOrderDto
        {
            Id = order.Id,
            OrderDate = order.OrderDate,
            SupplierId = order.SupplierId,
            Items = items.Select(i => new PurchaseOrderItemDto
            {
                ProductId = i.ProductId,
                ProductName = productNamesById.GetValueOrDefault(i.ProductId),
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };
    }
}
