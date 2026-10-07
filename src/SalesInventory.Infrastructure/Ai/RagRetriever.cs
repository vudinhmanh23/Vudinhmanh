using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesInventory.Application.Interfaces;
using SalesInventory.Infrastructure.Persistence;

namespace SalesInventory.Infrastructure.Ai;

// Finds the knowledge passages closest to a question: embeds the question, then ranks every stored chunk by cosine similarity.
// A full scan is fine for a shop's policy documents (hundreds of chunks); past tens of thousands, move to a vector index.
public sealed class RagRetriever : IKnowledgeRetriever
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingService _embeddings;
    private readonly KnowledgeOptions _options;

    public RagRetriever(AppDbContext db, IEmbeddingService embeddings, IOptions<KnowledgeOptions> options)
    {
        _db = db;
        _embeddings = embeddings;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<KnowledgeHit>> SearchAsync(string query, int topK, CancellationToken cancellationToken = default)
    {
        // No embeddings key: there is nothing to search with, and every question would otherwise log the same error
        if (string.IsNullOrWhiteSpace(query) || topK <= 0 || !_embeddings.IsConfigured)
        {
            return Array.Empty<KnowledgeHit>();
        }

        var queryVector = await _embeddings.EmbedAsync(query, EmbeddingInputType.Query, cancellationToken);

        var chunks = await _db.KnowledgeChunks.AsNoTracking()
            .Select(c => new { c.SourceTitle, c.Content, c.Embedding })
            .ToListAsync(cancellationToken);

        return chunks
            .Select(c => new KnowledgeHit(c.SourceTitle, c.Content, VectorMath.CosineSimilarity(queryVector, VectorMath.FromBytes(c.Embedding))))
            .Where(h => h.Score >= _options.MinScore)
            .OrderByDescending(h => h.Score)
            .Take(topK)
            .ToList();
    }
}
