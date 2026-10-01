using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;
using SalesInventory.Domain.Entities;
using SalesInventory.Infrastructure.Identity;

namespace SalesInventory.Api.Controllers;

// Suppliers: whole controller restricted to Admin and Kho
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Kho}")]
public class SuppliersController : ControllerBase
{
    private readonly ISupplierService _supplierService;

    public SuppliersController(ISupplierService supplierService)
    {
        _supplierService = supplierService;
    }

    /// <summary>Gets all suppliers.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SupplierDto>>> GetSuppliers()
    {
        var suppliers = await _supplierService.GetSuppliersAsync();
        return Ok(suppliers.Select(ToDto));
    }

    /// <summary>Gets a single supplier by id.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<SupplierDto>> GetSupplier(int id)
    {
        var supplier = await _supplierService.GetSupplierAsync(id);
        if (supplier is null)
        {
            return NotFound();
        }

        return Ok(ToDto(supplier));
    }

    /// <summary>Creates a new supplier.</summary>
    [HttpPost]
    public async Task<ActionResult<SupplierDto>> CreateSupplier(CreateSupplierDto dto)
    {
        var supplier = new Supplier { Name = dto.Name, Phone = dto.Phone, Email = dto.Email, Address = dto.Address };

        try
        {
            var created = await _supplierService.CreateSupplierAsync(supplier);
            return CreatedAtAction(nameof(GetSupplier), new { id = created.Id }, ToDto(created));
        }
        catch (ArgumentException ex)
        {
            // Business validation failure from the service layer
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Updates an existing supplier.</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateSupplier(int id, UpdateSupplierDto dto)
    {
        var supplier = new Supplier { Name = dto.Name, Phone = dto.Phone, Email = dto.Email, Address = dto.Address };

        try
        {
            var updated = await _supplierService.UpdateSupplierAsync(id, supplier);
            return updated ? NoContent() : NotFound();
        }
        catch (ArgumentException ex)
        {
            // Business validation failure from the service layer
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Deletes a supplier.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteSupplier(int id)
    {
        try
        {
            var deleted = await _supplierService.DeleteSupplierAsync(id);
            return deleted ? NoContent() : NotFound();
        }
        catch (DbUpdateException)
        {
            // Products or purchase orders still reference this supplier (FK is Restrict)
            return Conflict(new { message = "Không thể xóa nhà cung cấp đang được sản phẩm hoặc đơn nhập kho sử dụng." });
        }
    }

    private static SupplierDto ToDto(Supplier supplier)
    {
        return new SupplierDto
        {
            Id = supplier.Id,
            Name = supplier.Name,
            Phone = supplier.Phone,
            Email = supplier.Email,
            Address = supplier.Address
        };
    }
}
