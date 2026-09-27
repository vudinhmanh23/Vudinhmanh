using SalesInventory.Api.Models;
using SalesInventory.Api.Repositories;

namespace SalesInventory.Api.Services;

public class CategoryService : ICategoryService
{
    private readonly IRepository<Category> _categoryRepository;

    public CategoryService(IRepository<Category> categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    public async Task<IEnumerable<Category>> GetCategoriesAsync()
    {
        return await _categoryRepository.GetAllAsync();
    }

    public async Task<Category?> GetCategoryAsync(int id)
    {
        return await _categoryRepository.GetByIdAsync(id);
    }

    public async Task<Category> CreateCategoryAsync(Category category)
    {
        // Business rule: a category must have a non-empty name
        if (string.IsNullOrWhiteSpace(category.Name))
        {
            throw new ArgumentException("Category name cannot be empty.", nameof(category));
        }

        await _categoryRepository.AddAsync(category);
        await _categoryRepository.SaveChangesAsync();

        return category;
    }

    public async Task<bool> UpdateCategoryAsync(int id, Category category)
    {
        var existing = await _categoryRepository.GetByIdAsync(id);
        if (existing is null)
        {
            return false;
        }

        // Business rule: a category must have a non-empty name
        if (string.IsNullOrWhiteSpace(category.Name))
        {
            throw new ArgumentException("Category name cannot be empty.", nameof(category));
        }

        existing.Name = category.Name;
        existing.Description = category.Description;

        _categoryRepository.Update(existing);
        await _categoryRepository.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteCategoryAsync(int id)
    {
        var existing = await _categoryRepository.GetByIdAsync(id);
        if (existing is null)
        {
            return false;
        }

        _categoryRepository.Delete(existing);
        await _categoryRepository.SaveChangesAsync();

        return true;
    }
}
