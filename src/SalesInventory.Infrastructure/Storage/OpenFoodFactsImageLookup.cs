using System.Net;
using System.Text.Json;
using SalesInventory.Application.Interfaces;
using SalesInventory.Application.Services;

namespace SalesInventory.Infrastructure.Storage;

// Looks a product picture up on Open Food Facts (free, no API key) by barcode. Best for packaged consumer goods.
public sealed class OpenFoodFactsImageLookup : IProductImageLookup
{
    private readonly HttpClient _client;

    public OpenFoodFactsImageLookup(HttpClient client)
    {
        _client = client;
    }

    public async Task<Uri?> FindByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
    {
        // Digits only: the barcode becomes part of the request path, so nothing else may get through
        if (barcode.Length is < 6 or > 14 || !barcode.All(char.IsAsciiDigit))
        {
            return null;
        }

        try
        {
            using var response = await _client.GetAsync($"api/v2/product/{barcode}.json?fields=image_front_url,image_url", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (!json.RootElement.TryGetProperty("product", out var product) || product.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var field in new[] { "image_front_url", "image_url" })
            {
                if (product.TryGetProperty(field, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri)
                    && uri.Scheme == Uri.UriSchemeHttps)
                {
                    return uri;
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new ImageDownloadException("Không tra cứu được ảnh theo barcode (dịch vụ Open Food Facts không phản hồi).", isUpstreamError: true, inner: ex);
        }
    }
}
