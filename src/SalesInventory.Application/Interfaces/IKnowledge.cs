namespace SalesInventory.Application.Interfaces;

// Documents are embedded as "document"; user questions as "query" (providers such as Voyage use the difference)
public enum EmbeddingInputType
{
    Document,
    Query
}

// Turns text into vectors by calling an embeddings provider. Runs on the server only, so the provider key never reaches a client.
public interface IEmbeddingService
{
    // False when no provider key is configured; callers should skip retrieval instead of failing
    bool IsConfigured { get; }

    Task<float[]> EmbedAsync(string text, EmbeddingInputType inputType, CancellationToken cancellationToken = default);

    // Same order as the input; sends the texts in as few requests as the provider allows
    Task<IReadOnlyList<float[]>> EmbedManyAsync(IReadOnlyList<string> texts, EmbeddingInputType inputType, CancellationToken cancellationToken = default);
}

// A passage found for a question, with its cosine similarity (higher = closer)
public record KnowledgeHit(string SourceTitle, string Content, double Score);

public interface IKnowledgeRetriever
{
    // The topK most similar passages (best first); passages scoring below the configured minimum are dropped
    Task<IReadOnlyList<KnowledgeHit>> SearchAsync(string query, int topK, CancellationToken cancellationToken = default);
}

public record IngestionResult(int Documents, int Chunks, IReadOnlyList<string> Sources);

public interface IDocumentIngestionService
{
    // Reads every .md/.txt file in the knowledge folder, chunks and embeds it, and replaces the stored chunks of each document
    Task<IngestionResult> IngestAsync(CancellationToken cancellationToken = default);
}
