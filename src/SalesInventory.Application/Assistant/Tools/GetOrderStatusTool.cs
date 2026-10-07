using System.Globalization;
using System.Text.Json;
using SalesInventory.Application.Interfaces;
using SalesInventory.Domain.Enums;

namespace SalesInventory.Application.Assistant.Tools;

// get_order_status: status and total of one sales order. Deliberately returns no customer data and no line items.
public sealed class GetOrderStatusTool : IAssistantTool
{
    private const int MaxCodeLength = 30;
    private static readonly CultureInfo ViCulture = new("vi-VN");

    private readonly ISalesOrderService _orderService;

    public GetOrderStatusTool(ISalesOrderService orderService)
    {
        _orderService = orderService;
    }

    public string Name => "get_order_status";

    public string Description =>
        "Trả về trạng thái và tổng tiền của một đơn bán theo mã đơn, ví dụ SO-20261006-0001.";

    public string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "order_code": { "type": "string", "description": "Mã đơn hàng, ví dụ SO-20261006-0001" }
          },
          "required": ["order_code"],
          "additionalProperties": false
        }
        """;

    public IReadOnlyCollection<string> AllowedRoles => AssistantRoles.All;

    public async Task<ToolOutcome> ExecuteAsync(JsonElement input, CancellationToken cancellationToken)
    {
        if (input.ValueKind != JsonValueKind.Object
            || !input.TryGetProperty("order_code", out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return new ToolOutcome("Cần cung cấp order_code.", IsError: true);
        }

        var code = value.GetString()?.Trim().ToUpperInvariant();

        // Order numbers look like SO-20261006-0001; anything else cannot exist, so skip the database
        if (string.IsNullOrEmpty(code) || code.Length > MaxCodeLength || !code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            return new ToolOutcome("Mã đơn không hợp lệ.", IsError: true);
        }

        var order = await _orderService.GetOrderByNumberAsync(code);
        if (order is null)
        {
            return new ToolOutcome("Không tìm thấy đơn hàng với mã đã cho.", IsError: true);
        }

        return new ToolOutcome(AssistantJson.Serialize(new
        {
            orderCode = order.OrderNumber,
            status = order.Status == SalesOrderStatus.Cancelled ? "Đã hủy" : "Hoàn thành",
            orderDate = order.OrderDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            totalAmount = order.TotalAmount,
            totalFormatted = $"{order.TotalAmount.ToString("N0", ViCulture)} đ",
            itemCount = order.Items.Count
        }));
    }
}
