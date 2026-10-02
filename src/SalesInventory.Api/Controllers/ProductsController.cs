using SalesInventory.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesInventory.Application.Dtos;
using SalesInventory.Domain.Entities;
using SalesInventory.Application.Interfaces;

namespace SalesInventory.Api.Controllers;

// Products: GET open to any authenticated user; writes (incl. stock adjustment) require "CanManageInventory"
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProductsController : ControllerBase
{
    // CreateProductDto has no SupplierId (per API contract), so new products fall back to the seeded default supplier
    private const int DefaultSupplierId = 1;

    private readonly IProductService _productService;
    private readonly ICategoryService _categoryService;
    private readonly ISupplierService _supplierService;

    public ProductsController(IProductService productService, ICategoryService categoryService, ISupplierService supplierService)
    {
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

    /// <summary>Creates a new product.</summary>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.CanManageInventory)]
    public async Task<ActionResult<ProductDto>> CreateProduct(CreateProductDto dto)
    {
        var product = new Product
        {
            Name = dto.Name,
            Sku = dto.Sku,
            Price = dto.Price,
            StockQuantity = dto.StockQuantity,
            CategoryId = dto.CategoryId,
            SupplierId = DefaultSupplierId,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            var created = await _productService.CreateProductAsync(product);
            var category = await _categoryService.GetCategoryAsync(created.CategoryId);
            return CreatedAtAction(nameof(GetProduct), new { id = created.Id }, ToDto(created, category?.Name, await GetSupplierNameAsync(created.SupplierId)));
        }
        catch (ArgumentException ex)
        {
            // Business validation failure from the service layer
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Updates an existing product.</summary>
    [HttpPut("{id}")]
    [Authorize(Policy = AuthPolicies.CanManageInventory)]
    public async Task<IActionResult> UpdateProduct(int id, UpdateProductDto dto)
    {
        var product = new Product
        {
            Name = dto.Name,
            Sku = dto.Sku,
            Price = dto.Price,
            StockQuantity = dto.StockQuantity,
            CategoryId = dto.CategoryId
        };

        try
        {
            var updated = await _productService.UpdateProductAsync(id, product);
            return updated ? NoContent() : NotFound();
        }
        catch (ArgumentException ex)
        {
            // Business validation failure from the service layer
            return BadRequest(ex.Message);
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

    private static ProductDto ToDto(Product product, string? categoryName, string? supplierName)
    {
        return new ProductDto
        {
            Id = product.Id,
            Name = product.Name,
            Sku = product.Sku,
            Price = product.Price,
            StockQuantity = product.StockQuantity,
            CategoryId = product.CategoryId,
            CategoryName = categoryName,
            SupplierId = product.SupplierId,
            SupplierName = supplierName
        };
    }
}
