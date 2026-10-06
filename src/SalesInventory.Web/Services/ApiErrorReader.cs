using System.Net;
using System.Text.Json;

namespace SalesInventory.Web.Services;

/// <summary>A user-facing error: a headline plus optional detail lines (e.g. one per invalid field).</summary>
public record ApiError(string Message, IReadOnlyList<string> Details);

/// <summary>Turns a failed API response into a friendly Vietnamese message.</summary>
public static class ApiErrorReader
{
    /// <param name="conflict">Message for 409; defaults to the duplicate-SKU text used by the product forms.</param>
    /// <param name="serverConflictMessage">Show the API's own 409 text (its "detail") instead of a fixed message.</param>
    /// <param name="notFound">Message for 404; defaults to the product text.</param>
    public static async Task<ApiError> ReadAsync(HttpResponseMessage response, string? conflict = null, string? notFound = null, bool serverConflictMessage = false)
    {
        return response.StatusCode switch
        {
            HttpStatusCode.BadRequest => new ApiError("Dữ liệu chưa hợp lệ", await ReadValidationMessagesAsync(response)),
            HttpStatusCode.Conflict => new ApiError(await ConflictMessageAsync(response, conflict, serverConflictMessage), Array.Empty<string>()),
            HttpStatusCode.Forbidden => new ApiError("Bạn không có quyền thực hiện thao tác này", Array.Empty<string>()),
            HttpStatusCode.NotFound => new ApiError(notFound ?? "Không tìm thấy sản phẩm", Array.Empty<string>()),
            _ => new ApiError("Không thể lưu, vui lòng thử lại", Array.Empty<string>())
        };
    }

    private static async Task<string> ConflictMessageAsync(HttpResponseMessage response, string? conflict, bool useServerMessage)
    {
        if (useServerMessage)
        {
            try
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } text)
                {
                    return text;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // Not a ProblemDetails body; fall through to the fixed message
            }
        }

        return conflict ?? "Mã SKU đã tồn tại";
    }

    // The API answers 400 with ProblemDetails

    // The API answers 400 with ProblemDetails; field errors are under "errors": { "Field": ["message", ...] }
    private static async Task<IReadOnlyList<string>> ReadValidationMessagesAsync(HttpResponseMessage response)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            var messages = new List<string>();

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in errors.EnumerateObject())
                {
                    messages.AddRange(field.Value.EnumerateArray().Select(m => m.GetString()).OfType<string>());
                }
            }
            else if (root.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } text)
            {
                messages.Add(text);
            }

            return messages;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Not a JSON body we understand; the headline alone is shown
            return Array.Empty<string>();
        }
    }
}


