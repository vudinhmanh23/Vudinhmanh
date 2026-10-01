using Microsoft.AspNetCore.Authorization;
using SalesInventory.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using SalesInventory.Application.Dtos;
using SalesInventory.Domain.Entities;
using SalesInventory.Application.Interfaces;

namespace SalesInventory.Api.Controllers;

[Authorize(Policy = AuthPolicies.AdminOnly)]
[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categoryService;

    public CategoriesController(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    /// <summary>Gets all categories.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryDto>>> GetCategories()
    {
        var categories = await _categoryService.GetCategoriesAsync();
        return Ok(categories.Select(ToDto));
    }

    /// <summary>Gets a single category by id.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<CategoryDto>> GetCategory(int id)
    {
        var category = await _categoryService.GetCategoryAsync(id);
        if (category is null)
        {
            return NotFound();
        }

        return Ok(ToDto(category));
    }

    /// <summary>Creates a new category.</summary>
    [HttpPost]
    public async Task<ActionResult<CategoryDto>> CreateCategory(CreateCategoryDto dto)
    {
        var category = new Category
        {
            Name = dto.Name,
            Description = dto.Description
        };

        try
        {
            var created = await _categoryService.CreateCategoryAsync(category);
            return CreatedAtAction(nameof(GetCategory), new { id = created.Id }, ToDto(created));
        }
        catch (ArgumentException ex)
        {
            // Business validation failure from the service layer
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Updates an existing category.</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCategory(int id, UpdateCategoryDto dto)
    {
        var category = new Category
        {
            Name = dto.Name,
            Description = dto.Description
        };

        try
        {
            var updated = await _categoryService.UpdateCategoryAsync(id, category);
            return updated ? NoContent() : NotFound();
        }
        catch (ArgumentException ex)
        {
            // Business validation failure from the service layer
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Deletes a category.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var deleted = await _categoryService.DeleteCategoryAsync(id);
        return deleted ? NoContent() : NotFound();
    }

    private static CategoryDto ToDto(Category category)
    {
        return new CategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description
        };
    }
}
