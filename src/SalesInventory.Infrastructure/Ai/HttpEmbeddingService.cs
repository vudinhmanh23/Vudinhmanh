using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesInventory.Application.Exceptions;
using SalesInventory.Application.Interfaces;

namespace SalesInventory.Infrastructure.Ai;

// Calls an embeddings API (Voyage by default, or any OpenAI-compatible one) over HttpClient. Both answer
// { "data": [ { "index": 0, "embedding": [..] } ] }; Voyage additionally takes "input_type" (query / document).
// Server side only: the key is read from configuration and is only ever sent in the Authorization header.
public sealed class HttpEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _client;
    private readonly EmbeddingOptions _options;
    private readonly ILogger<HttpEmbeddingService> _logger;

    public HttpEmbeddingService(HttpClient client, IOptions<EmbeddingOptions> options, ILogger<HttpEmbeddingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<float[]> EmbedAsync(string text, EmbeddingInputType inputType, CancellationToken cancellationToken = default)
    {
        return (await EmbedManyAsync(new[] { text }, inputType, cancellationToken))[0];
    }

    public async Task<IReadOnlyList<float[]>> EmbedManyAsync(IReadOnlyList<string> texts, EmbeddingInputType inputType, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogError("Embeddings were requested but no key is configured (Embeddings:ApiKey or VOYAGE_API_KEY)");
            throw new AssistantUnavailableException("Dịch vụ embeddings chưa được cấu hình.");
        }

        var result = new List<float[]>(texts.Count);
        var batchSize = Math.Max(1, _options.BatchSize);
        for (var offset = 0; offset < texts.Count; offset += batchSize)
        {
            var batch = texts.Skip(offset).Take(batchSize).ToList();
            result.AddRange(await SendAsync(batch, inputType, cancellationToken));
        }

        return result;
    }

    private async Task<List<float[]>> SendAsync(List<string> batch, EmbeddingInputType inputType, CancellationToken cancellationToken)
    {
        var input = new JsonArray();
        foreach (var text in batch)
        {
            input.Add(text);
        }

        var body = new JsonObject
        {
            ["model"] = _options.ResolveModel(),
            ["input"] = input
        };
        if (_options.IsVoyage)
        {
            // Retrieval quality is better when the provider knows which side the text is on
            body["input_type"] = inputType == EmbeddingInputType.Query ? "query" : "document";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "embeddings") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "The embeddings request failed before a response arrived");
            throw new AssistantUnavailableException("Không kết nối được tới dịch vụ embeddings.", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // The provider's message goes to the log only (it can echo request details); callers get a generic text
                _logger.LogError("Embeddings provider returned {Status}: {Body}", (int)response.StatusCode, text.Length > 300 ? text[..300] : text);
                throw new AssistantUnavailableException("Dịch vụ embeddings tạm thời không khả dụng.");
            }

            try
            {
                using var json = JsonDocument.Parse(text);
                var vectors = json.RootElement.GetProperty("data").EnumerateArray()
                    .OrderBy(d => d.GetProperty("index").GetInt32())
                    .Select(d => d.GetProperty("embedding").EnumerateArray().Select(v => v.GetSingle()).ToArray())
                    .ToList();
                if (vectors.Count != batch.Count)
                {
                    throw new InvalidOperationException($"Expected {batch.Count} embeddings, got {vectors.Count}.");
                }

                return vectors;
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                _logger.LogError(ex, "The embeddings response could not be parsed");
                throw new AssistantUnavailableException("Dịch vụ embeddings trả về dữ liệu không hợp lệ.", ex);
            }
        }
    }
}
