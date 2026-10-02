using AutoMapper;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    private readonly IMapper _mapper;
    private readonly IValidator<CreateSupplierDto> _createValidator;
    private readonly IValidator<UpdateSupplierDto> _updateValidator;

    public SuppliersController(
        ISupplierService supplierService,
        IMapper mapper,
        IValidator<CreateSupplierDto> createValidator,
        IValidator<UpdateSupplierDto> updateValidator)
    {
        _supplierService = supplierService;
        _mapper = mapper;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>Gets all suppliers.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SupplierDto>>> GetSuppliers()
    {
        var suppliers = await _supplierService.GetSuppliersAsync();
        return Ok(_mapper.Map<IEnumerable<SupplierDto>>(suppliers));
    }

    /// <summary>Gets only the suppliers that are currently active.</summary>
    [HttpGet("active")]
    public async Task<ActionResult<IEnumerable<SupplierDto>>> GetActiveSuppliers()
    {
        var suppliers = await _supplierService.GetActiveSuppliersAsync();
        return Ok(_mapper.Map<IEnumerable<SupplierDto>>(suppliers));
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

        return Ok(_mapper.Map<SupplierDto>(supplier));
    }

    /// <summary>Creates a new supplier.</summary>
    [HttpPost]
    public async Task<ActionResult<SupplierDto>> CreateSupplier(CreateSupplierDto dto)
    {
        var validation = await _createValidator.ValidateAsync(dto);
        if (!validation.IsValid)
        {
            return ValidationFailure(validation);
        }

        var supplier = _mapper.Map<Supplier>(dto);

        try
        {
            var created = await _supplierService.CreateSupplierAsync(supplier);
            return CreatedAtAction(nameof(GetSupplier), new { id = created.Id }, _mapper.Map<SupplierDto>(created));
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
        var validation = await _updateValidator.ValidateAsync(dto);
        if (!validation.IsValid)
        {
            return ValidationFailure(validation);
        }

        var supplier = _mapper.Map<Supplier>(dto);

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

    /// <summary>Deactivates a supplier (soft delete: the record is kept, IsActive becomes false).</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteSupplier(int id)
    {
        var deleted = await _supplierService.DeleteSupplierAsync(id);
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
