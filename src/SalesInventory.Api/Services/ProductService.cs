using SalesInventory.Api.Models;
using SalesInventory.Api.Repositories;

namespace SalesInventory.Api.Services;

public class ProductService : IProductService
{
    private readonly IRepository<Product> _productRepository;
    private readonly IRepository<Category> _categoryRepository;

    public ProductService(IRepository<Product> productRepository, IRepository<Category> categoryRepository)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
    }

    public async Task<IEnumerable<Product>> GetProductsAsync()
    {
        return await _productRepository.GetAllAsync();
    }

    public async Task<Product?> GetProductAsync(int id)
    {
        return await _productRepository.GetByIdAsync(id);
    }

    public async Task<IEnumerable<Product>> GetProductsByCategoryIdAsync(int categoryId)
    {
        var products = await _productRepository.GetAllAsync();
        return products.Where(p => p.CategoryId == categoryId);
    }

    public async Task<Product> CreateProductAsync(Product product)
    {
        // Business rule: a product's price must be greater than zero
        if (product.Price <= 0)
        {
            throw new ArgumentException("Product price must be greater than zero.", nameof(product));
        }

        // Business rule: CategoryId must reference an existing category
        if (await _categoryRepository.GetByIdAsync(product.CategoryId) is null)
        {
            throw new ArgumentException($"Category with Id {product.CategoryId} does not exist.", nameof(product));
        }

        await _productRepository.AddAsync(product);
        await _productRepository.SaveChangesAsync();

        return product;
    }

    public async Task<bool> UpdateProductAsync(int id, Product product)
    {
        var existing = await _productRepository.GetByIdAsync(id);
        if (existing is null)
        {
            return false;
        }

        // Business rule: a product's price must be greater than zero
        if (product.Price <= 0)
        {
            throw new ArgumentException("Product price must be greater than zero.", nameof(product));
        }

        // Business rule: CategoryId must reference an existing category
        if (await _categoryRepository.GetByIdAsync(product.CategoryId) is null)
        {
            throw new ArgumentException($"Category with Id {product.CategoryId} does not exist.", nameof(product));
        }

        // SupplierId and CreatedAt are intentionally left untouched by updates
        existing.Name = product.Name;
        existing.Sku = product.Sku;
        existing.Price = product.Price;
        existing.StockQuantity = product.StockQuantity;
        existing.CategoryId = product.CategoryId;

        _productRepository.Update(existing);
        await _productRepository.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteProductAsync(int id)
    {
        var existing = await _productRepository.GetByIdAsync(id);
        if (existing is null)
        {
            return false;
        }

        _productRepository.Delete(existing);
        await _productRepository.SaveChangesAsync();

        return true;
    }
}
