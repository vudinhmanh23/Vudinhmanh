using SalesInventory.Domain.Entities;
using SalesInventory.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace SalesInventory.Application.Services;

public class ProductService : IProductService
{
    private readonly IRepository<Product> _productRepository;
    private readonly IRepository<Category> _categoryRepository;
    private readonly InventorySettings _inventorySettings;

    public ProductService(IRepository<Product> productRepository, IRepository<Category> categoryRepository, IOptions<InventorySettings> inventorySettings)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _inventorySettings = inventorySettings.Value;
    }

    public async Task<IEnumerable<Product>> GetProductsAsync()
    {
        return await _productRepository.GetAllAsync();
    }

    public async Task<Product?> GetProductAsync(int id)
    {
        return await _productRepository.GetByIdAsync(id);
    }

    public async Task<Product?> GetProductBySkuAsync(string sku)
    {
        var products = await _productRepository.GetAllAsync();
        return products.FirstOrDefault(p => string.Equals(p.Sku, sku, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IEnumerable<Product>> GetInactiveProductsAsync()
    {
        var products = await _productRepository.GetAllAsync();
        return products.Where(p => !p.IsActive);
    }

    public async Task<IEnumerable<Product>> GetLowStockProductsAsync()
    {
        var products = await _productRepository.GetAllAsync();
        return products
            .Where(p => p.StockQuantity < _inventorySettings.LowStockThreshold)
            .OrderBy(p => p.StockQuantity)
            .ThenBy(p => p.Id);
    }

    public async Task<bool> IsSkuTakenAsync(string sku, int? excludeProductId)
    {
        var products = await _productRepository.GetAllAsync();
        return products.Any(p => p.Id != excludeProductId && string.Equals(p.Sku, sku, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> IsBarcodeTakenAsync(string barcode, int? excludeProductId)
    {
        var products = await _productRepository.GetAllAsync();
        return products.Any(p => p.Id != excludeProductId && p.Barcode is not null && string.Equals(p.Barcode, barcode, StringComparison.OrdinalIgnoreCase));
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

        await EnsureCodesAreUniqueAsync(product, null);

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

        await EnsureCodesAreUniqueAsync(product, id);

        // SupplierId and CreatedAt are intentionally left untouched by updates
        existing.Name = product.Name;
        existing.Sku = product.Sku;
        existing.Barcode = product.Barcode;
        existing.Description = product.Description;
        existing.Unit = product.Unit;
        existing.Price = product.Price;
        existing.PurchasePrice = product.PurchasePrice;
        existing.SalePrice = product.SalePrice;
        existing.IsActive = product.IsActive;
        existing.StockQuantity = product.StockQuantity;
        existing.LowStockThreshold = product.LowStockThreshold;
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

    // Friendly pre-check; the unique indexes on Products.Sku / Products.Barcode remain the final guard against races
    private async Task EnsureCodesAreUniqueAsync(Product product, int? excludeId)
    {
        if (await IsSkuTakenAsync(product.Sku, excludeId))
        {
            throw new DuplicateSkuException(product.Sku);
        }

        if (product.Barcode is not null && await IsBarcodeTakenAsync(product.Barcode, excludeId))
        {
            throw new DuplicateBarcodeException(product.Barcode);
        }
    }
}
