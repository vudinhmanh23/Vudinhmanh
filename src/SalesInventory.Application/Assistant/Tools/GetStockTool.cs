using System.Text.Json;
using SalesInventory.Application.Interfaces;

namespace SalesInventory.Application.Assistant.Tools;

// get_stock: current stock of one product
public sealed class GetStockTool : IAssistantTool
{
    private readonly ProductLookup _lookup;

    public GetStockTool(IProductService productService)
    {
        _lookup = new ProductLookup(productService);
    }

    public string Name => "get_stock";

    public string Description =>
        "Trả về số lượng tồn kho hiện tại của MỘT sản phẩm. Cung cấp sku (ưu tiên) hoặc product_name. " +
        "Nếu tên khớp nhiều sản phẩm, kết quả là danh sách tối đa 5 ứng viên để hỏi lại khách.";

    public string InputSchemaJson => AssistantSchemas.ProductSelector;

    public IReadOnlyCollection<string> AllowedRoles => AssistantRoles.All;

    public async Task<ToolOutcome> ExecuteAsync(JsonElement input, CancellationToken cancellationToken)
    {
        var result = await _lookup.ResolveAsync(input);
        if (result.Product is not { } product)
        {
            return result.Failure!;
        }

        var status = product.StockQuantity <= 0
            ? "hết hàng"
            : product.StockQuantity <= product.LowStockThreshold ? "sắp hết" : "còn hàng";

        return new ToolOutcome(AssistantJson.Serialize(new
        {
            sku = product.Sku,
            name = product.Name,
            stockQuantity = product.StockQuantity,
            unit = product.Unit,
            status,
            discontinued = !product.IsActive
        }));
    }
}
