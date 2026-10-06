using FluentValidation;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;

namespace SalesInventory.Application.Validators;

// Shared rules for the write models of a product
internal static class ProductRules
{
    public static void Apply<T>(
        AbstractValidator<T> validator,
        Func<T, string> name,
        Func<T, string> sku,
        Func<T, string?> description,
        Func<T, string?> barcode,
        Func<T, string> unit,
        Func<T, decimal> purchasePrice,
        Func<T, decimal> salePrice,
        Func<T, int> quantity)
    {
        validator.RuleFor(x => name(x)).NotEmpty().MaximumLength(200).OverridePropertyName("Name");
        validator.RuleFor(x => sku(x))
            .NotEmpty()
            .Matches("^[A-Z0-9-]{3,32}$")
            .WithMessage("SKU chỉ gồm chữ in hoa, số hoặc dấu '-', dài 3-32 ký tự")
            .OverridePropertyName("Sku");
        validator.RuleFor(x => description(x)).MaximumLength(1000).OverridePropertyName("Description");
        validator.RuleFor(x => barcode(x))
            .MaximumLength(50)
            .Must(b => b is null || !string.IsNullOrWhiteSpace(b))
            .WithMessage("Barcode không được là chuỗi rỗng (bỏ trống thì để null)")
            .OverridePropertyName("Barcode");
        validator.RuleFor(x => unit(x)).NotEmpty().MaximumLength(50).OverridePropertyName("Unit");
        validator.RuleFor(x => purchasePrice(x)).GreaterThanOrEqualTo(0).OverridePropertyName("PurchasePrice");
        validator.RuleFor(x => salePrice(x)).GreaterThan(0).OverridePropertyName("SalePrice");
        validator.RuleFor(x => salePrice(x))
            .GreaterThanOrEqualTo(x => purchasePrice(x))
            .WithMessage("Giá bán không được nhỏ hơn giá nhập")
            .OverridePropertyName("SalePrice");
        validator.RuleFor(x => quantity(x)).GreaterThanOrEqualTo(0).OverridePropertyName("Quantity");
    }
}

public class CreateProductDtoValidator : AbstractValidator<CreateProductDto>
{
    public CreateProductDtoValidator(IProductService productService)
    {
        ProductRules.Apply(this, x => x.Name, x => x.Sku, x => x.Description, x => x.Barcode, x => x.Unit, x => x.PurchasePrice, x => x.SalePrice, x => x.Quantity);
        RuleFor(x => x.LowStockThreshold).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ReorderLevel).GreaterThanOrEqualTo(0);

        // Block duplicate SKUs at the validation layer (the lookup goes through the service, not DbContext, to keep layers clean)
        RuleFor(x => x.Sku)
            .MustAsync(async (sku, _) => !await productService.IsSkuTakenAsync(sku, null))
            .When(x => !string.IsNullOrWhiteSpace(x.Sku))
            .WithMessage(x => $"SKU '{x.Sku}' đã tồn tại.");
    }
}

public class UpdateProductDtoValidator : AbstractValidator<UpdateProductDto>
{
    // The controller passes the id of the product being edited through ValidationContext.RootContextData
    public const string ProductIdKey = "ProductId";

    public UpdateProductDtoValidator(IProductService productService)
    {
        ProductRules.Apply(this, x => x.Name, x => x.Sku, x => x.Description, x => x.Barcode, x => x.Unit, x => x.PurchasePrice ?? 0, x => x.SalePrice, x => x.Quantity);
        RuleFor(x => x.LowStockThreshold).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ReorderLevel).GreaterThanOrEqualTo(0);

        // Changing the SKU/barcode to one that belongs to a different product is a validation error (400)
        RuleFor(x => x.Sku)
            .MustAsync(async (_, sku, context, __) => !await productService.IsSkuTakenAsync(sku, ProductId(context)))
            .When(x => !string.IsNullOrWhiteSpace(x.Sku))
            .WithMessage(x => $"SKU '{x.Sku}' đã được dùng cho sản phẩm khác.");

        RuleFor(x => x.Barcode)
            .MustAsync(async (_, barcode, context, __) => !await productService.IsBarcodeTakenAsync(barcode!, ProductId(context)))
            .When(x => !string.IsNullOrWhiteSpace(x.Barcode))
            .WithMessage(x => $"Barcode '{x.Barcode}' đã được dùng cho sản phẩm khác.");
    }

    private static int? ProductId(ValidationContext<UpdateProductDto> context)
    {
        return context.RootContextData.TryGetValue(ProductIdKey, out var id) ? id as int? : null;
    }
}
