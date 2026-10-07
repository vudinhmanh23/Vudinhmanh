using SalesInventory.Application.Dtos;
using SalesInventory.Application.Exceptions;
using SalesInventory.Domain.Entities;
using SalesInventory.Application.Interfaces;

namespace SalesInventory.Application.Services;

public class ProductService : IProductService
{
    private readonly IRepository<Product> _productRepository;
    private readonly IRepository<Category> _categoryRepository;
    private readonly IProductRepository _productQueries;

    public ProductService(IRepository<Product> productRepository, IRepository<Category> categoryRepository, IProductRepository productQueries)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _productQueries = productQueries;
    }

    public async Task<IEnumerable<Product>> GetProductsAsync()
    {
        return await _productRepository.GetAllAsync();
    }

    public async Task<Product?> GetProductAsync(int id)
    {
        return await _productRepository.GetByIdAsync(id);
    }

    public async Task<Product?> GetProductReadOnlyAsync(int id)
    {
        return await _productQueries.GetByIdReadOnlyAsync(id);
    }

    public async Task<Product?> GetProductBySkuAsync(string sku)
    {
        return await _productQueries.GetBySkuAsync(sku);
    }

    public async Task<IEnumerable<Product>> GetInactiveProductsAsync()
    {
        return await _productQueries.GetInactiveAsync();
    }

    public async Task<IReadOnlyList<Product>> GetLowStockProductsAsync(bool includeInactive = false)
    {
        return await _productQueries.GetBelowReorderLevelAsync(includeInactive);
    }

    public async Task<(IReadOnlyList<Product> Items, int TotalCount)> SearchProductsAsync(
        string? search, int? categoryId, string sortBy, bool descending, int page, int pageSize)
    {
        return await _productQueries.SearchAsync(search, categoryId, sortBy, descending, page, pageSize);
    }

    public async Task<(IReadOnlyList<ProductListItemDto> Items, int TotalCount)> QueryProductsAsync(ProductQueryParameters query)
    {
        return await _productQueries.QueryAsync(query);
    }

    public async Task<InventorySummary> GetInventorySummaryAsync()
    {
        return await _productQueries.GetInventorySummaryAsync();
    }

    public async Task<bool> IsSkuTakenAsync(string sku, int? excludeProductId)
    {
        // The database compares with the column collation (case-insensitive) and uses the unique index on Sku
        return await _productRepository.AnyAsync(p => p.Id != excludeProductId && p.Sku == sku);
    }

    public async Task<bool> IsBarcodeTakenAsync(string barcode, int? excludeProductId)
    {
        return await _productRepository.AnyAsync(p => p.Id != excludeProductId && p.Barcode == barcode);
    }

    public async Task<IEnumerable<Product>> GetProductsByCategoryIdAsync(int categoryId)
    {
        return await _productQueries.GetByCategoryAsync(categoryId);
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
        existing.ReorderLevel = product.ReorderLevel;
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

        // A product with orders or stock history must stay: those rows (and the stock ledger) refer to it.
        // Without this check the database's foreign key would fail the delete and the API would answer 500.
        if (await _productQueries.HasDocumentsAsync(id))
        {
            throw new ConflictException(
                $"Không thể xóa sản phẩm {existing.Name} (Id {id}): sản phẩm đã có đơn hàng hoặc lịch sử kho. Hãy chuyển sang ngừng kinh doanh (IsActive = false).");
        }

        _productRepository.Delete(existing);
        await _productRepository.SaveChangesAsync();

        return true;
    }

    public async Task<(bool Found, string? PreviousImageUrl)> SetProductImageAsync(int id, string? imageUrl)
    {
        var existing = await _productRepository.GetByIdAsync(id);
        if (existing is null)
        {
            return (false, null);
        }

        var previous = existing.ImageUrl;
        existing.ImageUrl = imageUrl;

        _productRepository.Update(existing);
        await _productRepository.SaveChangesAsync();

        return (true, previous);
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
