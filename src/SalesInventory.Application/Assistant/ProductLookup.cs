using System.Text.Json;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;
using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Assistant;

// Resolves the (sku | product_name) arguments shared by get_stock and get_price into one product, or explains why not
public class ProductLookup
{
    public const int MaxCandidates = 5;
    private const int MaxTextLength = 100;

    private readonly IProductService _productService;

    public ProductLookup(IProductService productService)
    {
        _productService = productService;
    }

    public record Candidate(string Sku, string Name);

    // Exactly one of Product / Failure is set; Failure is the finished tool outcome to return as is
    public record Result(Product? Product, ToolOutcome? Failure);

    public async Task<Result> ResolveAsync(JsonElement input)
    {
        var sku = ReadText(input, "sku");
        var name = ReadText(input, "product_name");

        if (sku is null && name is null)
        {
            return Fail("Cần cung cấp sku hoặc product_name.");
        }

        // The SKU is exact and unique, so it wins when both are given
        if (sku is not null)
        {
            var bySku = await _productService.GetProductBySkuAsync(sku);
            return bySku is null ? Fail("Không tìm thấy sản phẩm với SKU đã cho.") : new Result(bySku, null);
        }

        var (items, _) = await _productService.QueryProductsAsync(new ProductQueryParameters
        {
            Keyword = name,
            SortBy = "name",
            PageSize = MaxCandidates + 1
        });

        // The keyword search also looks into descriptions; only a name or SKU hit counts as the product asked for
        var matches = items
            .Where(p => p.Name.Contains(name!, StringComparison.OrdinalIgnoreCase) || p.Sku.Contains(name!, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            return Fail("Không tìm thấy sản phẩm nào có tên như vậy.");
        }

        // An exact name match settles it even when other products merely contain the text
        var exact = matches.Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1)
        {
            matches = exact;
        }

        if (matches.Count > 1)
        {
            var candidates = matches.Take(MaxCandidates).Select(p => new Candidate(p.Sku, p.Name)).ToList();
            var json = AssistantJson.Serialize(new { ambiguous = true, message = "Có nhiều sản phẩm phù hợp, hãy hỏi khách chọn một.", candidates });
            return new Result(null, new ToolOutcome(json));
        }

        // The list row has no reorder threshold, so load the full product for the single match
        var product = await _productService.GetProductReadOnlyAsync(matches[0].Id);
        return product is null ? Fail("Không tìm thấy sản phẩm nào có tên như vậy.") : new Result(product, null);
    }

    // A string argument written by the model: must be a non-empty string; trimmed and capped in length
    private static string? ReadText(JsonElement input, string property)
    {
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length > MaxTextLength ? text[..MaxTextLength] : text;
    }

    private static Result Fail(string message) => new(null, new ToolOutcome(message, IsError: true));
}
