using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Interfaces;

// Business-facing operations for Category, on top of the repository layer
public interface ICategoryService
{
    Task<IEnumerable<Category>> GetCategoriesAsync();
    Task<Category?> GetCategoryAsync(int id);
    Task<Category> CreateCategoryAsync(Category category);
    Task<bool> UpdateCategoryAsync(int id, Category category);
    Task<bool> DeleteCategoryAsync(int id);
}
