using System.Globalization;
using System.Text.Json;
using SalesInventory.Application.Interfaces;

namespace SalesInventory.Application.Assistant.Tools;

// get_price: current sale price of one product (never the purchase price)
public sealed class GetPriceTool : IAssistantTool
{
    private static readonly CultureInfo ViCulture = new("vi-VN");

    private readonly ProductLookup _lookup;

    public GetPriceTool(IProductService productService)
    {
        _lookup = new ProductLookup(productService);
    }

    public string Name => "get_price";

    public string Description =>
        "Trả về giá bán hiện tại (VND) của MỘT sản phẩm. Cung cấp sku (ưu tiên) hoặc product_name. " +
        "Nếu tên khớp nhiều sản phẩm, kết quả là danh sách tối đa 5 ứng viên để hỏi lại khách. Không trả giá nhập.";

    public string InputSchemaJson => AssistantSchemas.ProductSelector;

    public IReadOnlyCollection<string> AllowedRoles => AssistantRoles.All;

    public async Task<ToolOutcome> ExecuteAsync(JsonElement input, CancellationToken cancellationToken)
    {
        var result = await _lookup.ResolveAsync(input);
        if (result.Product is not { } product)
        {
            return result.Failure!;
        }

        return new ToolOutcome(AssistantJson.Serialize(new
        {
            sku = product.Sku,
            name = product.Name,
            salePrice = product.SalePrice,
            // Pre-formatted so the model quotes the price exactly instead of formatting it itself
            salePriceFormatted = $"{product.SalePrice.ToString("N0", ViCulture)} đ",
            discontinued = !product.IsActive
        }));
    }
}
