using AutoMapper;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SalesInventory.Application.Services;
using SalesInventory.Application.Validators;
using SalesInventory.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesInventory.Application.Dtos;
using SalesInventory.Domain.Entities;
using SalesInventory.Application.Interfaces;

namespace SalesInventory.Api.Controllers;

// Products: GET open to any authenticated user (except stock history); writes and stock history require "CanManageInventory"
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProductsController : ControllerBase
{
    // CreateProductDto has no SupplierId (per API contract), so new products fall back to the seeded default supplier
    private const int DefaultSupplierId = 1;

    // Upper bound for the list page size, same as the customers list
    private const int MaxPageSize = 100;

    private readonly IProductService _productService;
    private readonly ICategoryService _categoryService;
    private readonly ISupplierService _supplierService;
    private readonly IStockMovementService _stockMovementService;
    private readonly IMapper _mapper;
    private readonly IValidator<CreateProductDto> _createValidator;
    private readonly IValidator<UpdateProductDto> _updateValidator;

    public ProductsController(
        IProductService productService,
        ICategoryService categoryService,
        ISupplierService supplierService,
        IStockMovementService stockMovementService,
        IMapper mapper,
        IValidator<CreateProductDto> createValidator,
        IValidator<UpdateProductDto> updateValidator)
    {
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _stockMovementService = stockMovementService;
        _mapper = mapper;
        _productService = productService;
        _categoryService = categoryService;
        _supplierService = supplierService;
    }

    /// <summary>Gets all products.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetProducts()
    {
        var products = await _productService.GetProductsAsync();
        var categories = await _categoryService.GetCategoriesAsync();
        var categoryNamesById = categories.ToDictionary(c => c.Id, c => c.Name);

        var suppliers = await _supplierService.GetSuppliersAsync();
        var supplierNamesById = suppliers.ToDictionary(s => s.Id, s => s.Name);

        return Ok(products.Select(p => ToDto(p, categoryNamesById.GetValueOrDefault(p.CategoryId), SupplierName(p, supplierNamesById))));
    }

    /// <summary>
    /// Gets one page of products with optional name search, category filter and sorting (all done in the database).
    /// sortBy: name | price; sortDir: asc | desc.
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<PagedResult<ProductDto>>> SearchProducts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] int? categoryId = null,
        [FromQuery] string sortBy = "name",
        [FromQuery] string sortDir = "asc")
    {
        if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
        {
            ModelState.AddModelError(nameof(page), $"page must be >= 1 and pageSize must be between 1 and {MaxPageSize}.");
            return ValidationProblem(ModelState);
        }

        var sortKey = sortBy.ToLowerInvariant();
        var dirKey = sortDir.ToLowerInvariant();
        if (sortKey is not ("name" or "price") || dirKey is not ("asc" or "desc"))
        {
            ModelState.AddModelError(nameof(sortBy), "sortBy must be 'name' or 'price' and sortDir must be 'asc' or 'desc'.");
            return ValidationProblem(ModelState);
        }

        var (products, totalCount) = await _productService.SearchProductsAsync(
            search, categoryId, sortKey, dirKey == "desc", page, pageSize);

        var categories = await _categoryService.GetCategoriesAsync();
        var categoryNamesById = categories.ToDictionary(c => c.Id, c => c.Name);
        var suppliers = await _supplierService.GetSuppliersAsync();
        var supplierNamesById = suppliers.ToDictionary(s => s.Id, s => s.Name);

        return Ok(new PagedResult<ProductDto>
        {
            Items = products.Select(p => ToDto(p, categoryNamesById.GetValueOrDefault(p.CategoryId), SupplierName(p, supplierNamesById))).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    /// <summary>Gets all products belonging to a given category.</summary>
    [HttpGet("by-category/{categoryId}")]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetProductsByCategory(int categoryId)
    {
        var products = await _productService.GetProductsByCategoryIdAsync(categoryId);
        var category = await _categoryService.GetCategoryAsync(categoryId);

        var suppliers = await _supplierService.GetSuppliersAsync();
        var supplierNamesById = suppliers.ToDictionary(s => s.Id, s => s.Name);

        return Ok(products.Select(p => ToDto(p, category?.Name, SupplierName(p, supplierNamesById))));
    }

    /// <summary>Gets a single product by its SKU (case-insensitive).</summary>
    [HttpGet("by-sku/{sku}")]
    public async Task<ActionResult<ProductDto>> GetProductBySku(string sku)
    {
        var product = await _productService.GetProductBySkuAsync(sku);
        if (product is null)
        {
            return NotFound();
        }

        var category = await _categoryService.GetCategoryAsync(product.CategoryId);
        var supplierName = await GetSupplierNameAsync(product.SupplierId);
        return Ok(ToDto(product, category?.Name, supplierName));
    }

    /// <summary>Gets discontinued products (IsActive = false).</summary>
    [HttpGet("inactive")]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetInactiveProducts()
    {
        var products = await _productService.GetInactiveProductsAsync();
        var categories = await _categoryService.GetCategoriesAsync();
        var categoryNamesById = categories.ToDictionary(c => c.Id, c => c.Name);

        var suppliers = await _supplierService.GetSuppliersAsync();
        var supplierNamesById = suppliers.ToDictionary(s => s.Id, s => s.Name);

        return Ok(products.Select(p => ToDto(p, categoryNamesById.GetValueOrDefault(p.CategoryId), SupplierName(p, supplierNamesById))));
    }

    /// <summary>
    /// Low-stock report: active products with ReorderLevel &gt; 0 whose stock is at or below it,
    /// with Shortage = ReorderLevel - StockQuantity, biggest shortage first. Read-only.
    /// Discontinued (inactive) products are left out unless <c>includeInactive=true</c> (useful for a stocktake).
    /// </summary>
    [HttpGet("low-stock")]
    public async Task<ActionResult<IEnumerable<LowStockItemDto>>> GetLowStockProducts([FromQuery] bool includeInactive = false)
    {
        var products = await _productService.GetLowStockProductsAsync(includeInactive);

        return Ok(products.Select(LowStockItemDto.From));
    }

    /// <summary>Gets a single product by id.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ProductDto>> GetProduct(int id)
    {
        var product = await _productService.GetProductAsync(id);
        if (product is null)
        {
            return NotFound();
        }

        var category = await _categoryService.GetCategoryAsync(product.CategoryId);
        var supplierName = await GetSupplierNameAsync(product.SupplierId);
        return Ok(ToDto(product, category?.Name, supplierName));
    }

    /// <summary>Gets the stock movement history of a product, newest first.</summary>
    [HttpGet("{id}/movements")]
    [Authorize(Policy = AuthPolicies.CanManageInventory)]
    public async Task<ActionResult<IEnumerable<StockMovementDto>>> GetProductMovements(int id)
    {
        var movements = await _stockMovementService.GetProductMovementsAsync(id);
        if (movements is null)
        {
            return NotFound();
        }

        return Ok(_mapper.Map<IEnumerable<StockMovementDto>>(movements));
    }

    /// <summary>
    /// Manually corrects a product's stock by a signed delta (e.g. after a stocktake) and logs an Adjustment movement.
    /// Returns 409 when the result would be below zero.
    /// </summary>
    [HttpPost("{id}/adjust-stock")]
    [Authorize(Policy = AuthPolicies.CanManageInventory)]
    public async Task<ActionResult<StockAdjustmentDto>> AdjustStock(int id, AdjustStockDto dto)
    {
        var result = await _stockMovementService.AdjustStockAsync(id, dto.Delta, dto.Reason);
        return Ok(new StockAdjustmentDto
        {
            ProductId = id,
            PreviousQuantity = result.PreviousQuantity,
            NewQuantity = result.NewQuantity,
            Movement = _mapper.Map<StockMovementDto>(result.Movement)
        });
    }

    /// <summary>
    /// Stocktake: sets a product's stock to a counted number. The Adjustment movement records
    /// Quantity = new stock - old stock (negative or positive) and the reason you give.
    /// </summary>
    [HttpPost("{id}/set-stock")]
    [Authorize(Policy = AuthPolicies.CanManageInventory)]
    public async Task<ActionResult<StockAdjustmentDto>> SetStock(int id, SetStockDto dto)
    {
        var result = await _stockMovementService.SetStockAsync(id, dto.NewQuantity, dto.Reason);
        return Ok(new StockAdjustmentDto
        {
            ProductId = id,
            PreviousQuantity = result.PreviousQuantity,
            NewQuantity = result.NewQuantity,
            Movement = _mapper.Map<StockMovementDto>(result.Movement)
        });
    }

    /// <summary>Creates a new product.</summary>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.CanManageInventory)]
    public async Task<ActionResult<ProductDto>> CreateProduct(CreateProductDto dto)
    {
        var validation = await _createValidator.ValidateAsync(dto);
        if (!validation.IsValid)
        {
            return ValidationFailure(validation);
        }

        var product = _mapper.Map<Product>(dto);
        product.SupplierId = DefaultSupplierId;
        product.CreatedAt = DateTime.UtcNow;

        try
        {
            var created = await _productService.CreateProductAsync(product);
            var category = await _categoryService.GetCategoryAsync(created.CategoryId);
            return CreatedAtAction(nameof(GetProduct), new { id = created.Id }, ToDto(created, category?.Name, await GetSupplierNameAsync(created.SupplierId)));
        }
        catch (DuplicateSkuException ex)
        {
            return Problem(title: "SKU đã tồn tại", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (DuplicateBarcodeException ex)
        {
            return Problem(title: "Barcode đã tồn tại", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException)
        {
            // A unique index (SKU or barcode) fired, e.g. two concurrent requests passed the pre-check
            return Problem(title: "SKU hoặc Barcode đã tồn tại", detail: "SKU hoặc Barcode đã được dùng cho sản phẩm khác.", statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException ex)
        {
            // Business validation failure from the service layer
            return Problem(title: "Business rule violated", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Updates an existing product.</summary>
    [HttpPut("{id}")]
    [Authorize(Policy = AuthPolicies.CanManageInventory)]
    public async Task<IActionResult> UpdateProduct(int id, UpdateProductDto dto)
    {
        var validationContext = new ValidationContext<UpdateProductDto>(dto);
        validationContext.RootContextData[UpdateProductDtoValidator.ProductIdKey] = id;
        var validation = await _updateValidator.ValidateAsync(validationContext);
        if (!validation.IsValid)
        {
            return ValidationFailure(validation);
        }

        var product = _mapper.Map<Product>(dto);

        if (dto.PurchasePrice is null)
        {
            // Cost price omitted: keep the stored one, and still enforce "sale price >= cost price" against it
            var existing = await _productService.GetProductAsync(id);
            if (existing is null)
            {
                return NotFound();
            }

            if (dto.SalePrice < existing.PurchasePrice)
            {
                ModelState.AddModelError(nameof(dto.SalePrice), "Giá bán không được nhỏ hơn giá nhập");
                return ValidationProblem(ModelState);
            }

            product.PurchasePrice = existing.PurchasePrice;
        }

        try
        {
            var updated = await _productService.UpdateProductAsync(id, product);
            return updated ? NoContent() : NotFound();
        }
        catch (DuplicateSkuException ex)
        {
            return Problem(title: "SKU đã tồn tại", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (DuplicateBarcodeException ex)
        {
            return Problem(title: "Barcode đã tồn tại", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException)
        {
            // A unique index (SKU or barcode) fired, e.g. two concurrent requests passed the pre-check
            return Problem(title: "SKU hoặc Barcode đã tồn tại", detail: "SKU hoặc Barcode đã được dùng cho sản phẩm khác.", statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException ex)
        {
            // Business validation failure from the service layer
            return Problem(title: "Business rule violated", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Deletes a product.</summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = AuthPolicies.CanManageInventory)]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        var deleted = await _productService.DeleteProductAsync(id);
        return deleted ? NoContent() : NotFound();
    }

    private static string? SupplierName(Product product, IReadOnlyDictionary<int, string> supplierNamesById)
    {
        return product.SupplierId is int id ? supplierNamesById.GetValueOrDefault(id) : null;
    }

    private async Task<string?> GetSupplierNameAsync(int? supplierId)
    {
        if (supplierId is null)
        {
            return null;
        }

        return (await _supplierService.GetSupplierAsync(supplierId.Value))?.Name;
    }

    private ProductDto ToDto(Product product, string? categoryName, string? supplierName)
    {
        var dto = _mapper.Map<ProductDto>(product);
        dto.CategoryName = categoryName;
        dto.SupplierName = supplierName;
        return dto;
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
