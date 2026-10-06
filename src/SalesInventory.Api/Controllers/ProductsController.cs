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

    // Image upload limits: 2 MB file; the request cap leaves room for multipart overhead
    private const long MaxImageRequestBytes = ProductImageRules.MaxBytes + 1024 * 1024;

    private readonly IProductService _productService;
    private readonly IFileStorage _fileStorage;
    private readonly IRemoteImageDownloader _imageDownloader;
    private readonly IProductImageLookup _imageLookup;
    private readonly ICategoryService _categoryService;
    private readonly ISupplierService _supplierService;
    private readonly IStockMovementService _stockMovementService;
    private readonly IMapper _mapper;
    private readonly IValidator<CreateProductDto> _createValidator;
    private readonly IValidator<UpdateProductDto> _updateValidator;

    public ProductsController(
        IProductService productService,
        IFileStorage fileStorage,
        IRemoteImageDownloader imageDownloader,
        IProductImageLookup imageLookup,
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
        _fileStorage = fileStorage;
        _imageDownloader = imageDownloader;
        _imageLookup = imageLookup;
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

    /// <summary>
    /// Uploads (or replaces) the product image. Accepts jpg/jpeg/png/webp up to 2 MB; the file is stored under a
    /// server-generated name and the original file name is ignored. Returns 400 for any rejected file (nothing is written).
    /// </summary>
    [HttpPost("{id}/image")]
    [Authorize(Policy = AuthPolicies.CanManageInventory)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxImageRequestBytes)]
    public async Task<ActionResult<ProductImageDto>> UploadImage(int id, IFormFile file, CancellationToken cancellationToken)
    {
        if (await _productService.GetProductAsync(id) is null)
        {
            return NotFound();
        }

        var error = await ValidateImageAsync(file, cancellationToken);
        if (error is not null)
        {
            return InvalidImage(error);
        }

        // Extension was validated against the whitelist above; the rest of the client's file name is never used
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        await using var stream = file.OpenReadStream();
        return await AttachImageAsync(id, stream, extension, cancellationToken);
    }

    /// <summary>
    /// Downloads the image at an https URL and sets it as the product image. The server refuses internal addresses
    /// (localhost, private networks, cloud metadata), and the downloaded bytes go through the same checks as an upload.
    /// </summary>
    [HttpPost("{id}/image-from-url")]
    [Authorize(Policy = AuthPolicies.CanManageInventory)]
    public async Task<ActionResult<ProductImageDto>> SetImageFromUrl(int id, ImageFromUrlDto dto, CancellationToken cancellationToken)
    {
        if (await _productService.GetProductAsync(id) is null)
        {
            return NotFound();
        }

        if (!Uri.TryCreate(dto.Url.Trim(), UriKind.Absolute, out var url))
        {
            return InvalidImage("Địa chỉ ảnh không hợp lệ.");
        }

        return await AttachRemoteImageAsync(id, url, cancellationToken);
    }

    /// <summary>
    /// Finds the product's picture on Open Food Facts by its barcode and sets it as the product image.
    /// 400 when the product has no barcode, 404 when the barcode has no picture online.
    /// </summary>
    [HttpPost("{id}/image-from-barcode")]
    [Authorize(Policy = AuthPolicies.CanManageInventory)]
    public async Task<ActionResult<ProductImageDto>> SetImageFromBarcode(int id, CancellationToken cancellationToken)
    {
        var product = await _productService.GetProductAsync(id);
        if (product is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(product.Barcode))
        {
            return InvalidImage("Sản phẩm chưa có barcode nên không thể tìm ảnh tự động.");
        }

        Uri? url;
        try
        {
            url = await _imageLookup.FindByBarcodeAsync(product.Barcode, cancellationToken);
        }
        catch (ImageDownloadException ex)
        {
            return ImageDownloadProblem(ex);
        }

        if (url is null)
        {
            return Problem(title: "Không tìm thấy ảnh", detail: $"Không có ảnh nào cho barcode {product.Barcode} trên Open Food Facts.", statusCode: StatusCodes.Status404NotFound);
        }

        // The URL comes from a third party, so it is downloaded through the same SSRF-safe path as a user-supplied one
        return await AttachRemoteImageAsync(id, url, cancellationToken);
    }

    private async Task<ActionResult<ProductImageDto>> AttachRemoteImageAsync(int id, Uri url, CancellationToken cancellationToken)
    {
        DownloadedImage image;
        try
        {
            image = await _imageDownloader.DownloadAsync(url, ProductImageRules.MaxBytes, cancellationToken);
        }
        catch (ImageDownloadException ex)
        {
            return ImageDownloadProblem(ex);
        }

        // The extension comes from the downloaded bytes themselves, never from the URL or the server's headers
        var extension = ProductImageRules.DetectExtension(image.Content.AsSpan(0, Math.Min(image.Content.Length, ProductImageRules.SignatureLength)));
        if (extension is null)
        {
            return InvalidImage("Nội dung tải về không phải là ảnh JPEG, PNG hoặc WebP hợp lệ.");
        }

        using var stream = new MemoryStream(image.Content);
        return await AttachImageAsync(id, stream, extension, cancellationToken);
    }

    // Saves an already validated image, points the product at it and removes the file it replaces
    private async Task<ActionResult<ProductImageDto>> AttachImageAsync(int id, Stream content, string extension, CancellationToken cancellationToken)
    {
        var imageUrl = await _fileStorage.SaveProductImageAsync(content, extension, cancellationToken);

        (bool Found, string? PreviousImageUrl) result;
        try
        {
            result = await _productService.SetProductImageAsync(id, imageUrl);
        }
        catch
        {
            // Don't leave an orphan file behind when the database update fails
            _fileStorage.DeleteProductImage(imageUrl);
            throw;
        }

        if (!result.Found)
        {
            // Product was deleted between the check and the update
            _fileStorage.DeleteProductImage(imageUrl);
            return NotFound();
        }

        _fileStorage.DeleteProductImage(result.PreviousImageUrl);
        return Ok(new ProductImageDto { ImageUrl = imageUrl });
    }

    private ActionResult InvalidImage(string detail) =>
        Problem(title: "Ảnh không hợp lệ", detail: detail, statusCode: StatusCodes.Status400BadRequest);

    // 502 when the remote side failed, 400 when the request itself was unacceptable (e.g. a blocked address)
    private ActionResult ImageDownloadProblem(ImageDownloadException ex) =>
        ex.IsUpstreamError
            ? Problem(title: "Không tải được ảnh", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway)
            : InvalidImage(ex.Message);

    // Returns an error message, or null when the upload is acceptable. Content type and extension are client-declared,
    // so the leading bytes are checked too (a renamed .txt must not pass).
    private static async Task<string?> ValidateImageAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return "Vui lòng chọn một file ảnh (trường 'file').";
        }

        if (file.Length > ProductImageRules.MaxBytes)
        {
            return "Ảnh vượt quá dung lượng tối đa 2 MB.";
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!ProductImageRules.IsAllowedExtension(extension) || !ProductImageRules.IsAllowedContentType(file.ContentType))
        {
            return "Chỉ chấp nhận ảnh JPEG, PNG hoặc WebP (.jpg, .jpeg, .png, .webp).";
        }

        var header = new byte[ProductImageRules.SignatureLength];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        if (!ProductImageRules.ContentMatchesExtension(header.AsSpan(0, read), extension))
        {
            return "Nội dung file không phải là ảnh hợp lệ.";
        }

        return null;
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
