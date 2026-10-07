namespace SalesInventory.Application.Assistant;

// Roles used to gate the assistant's tools (mirrors the roles of the API)
public static class AssistantRoles
{
    public const string Admin = "Admin";
    public const string Kho = "Kho";
    public const string BanHang = "BanHang";

    public static readonly string[] All = { Admin, Kho, BanHang };
}

// JSON Schemas shared by several tools
public static class AssistantSchemas
{
    // sku and product_name are both optional in the schema; the lookup requires at least one
    public const string ProductSelector = """
        {
          "type": "object",
          "properties": {
            "sku": { "type": "string", "description": "Mã SKU chính xác của sản phẩm, ví dụ SKU-DT-001" },
            "product_name": { "type": "string", "description": "Tên hoặc một phần tên sản phẩm" }
          },
          "additionalProperties": false
        }
        """;
}

// JSON written for the model: camelCase, and Vietnamese kept as readable text instead of \uXXXX escapes (fewer tokens)
public static class AssistantJson
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value, Options);
}
