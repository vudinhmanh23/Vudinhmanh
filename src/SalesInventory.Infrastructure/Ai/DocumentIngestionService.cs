using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesInventory.Application.Exceptions;
using SalesInventory.Application.Interfaces;
using SalesInventory.Domain.Entities;
using SalesInventory.Infrastructure.Persistence;

namespace SalesInventory.Infrastructure.Ai;

// Loads the knowledge documents: reads each .md/.txt file, cuts it into chunks, embeds them and stores them.
// Running it again replaces the chunks of every document it finds, so it is safe to repeat after editing a file.
public sealed class DocumentIngestionService : IDocumentIngestionService
{
    private static readonly string[] Extensions = { ".md", ".txt" };

    private readonly AppDbContext _db;
    private readonly IEmbeddingService _embeddings;
    private readonly KnowledgeOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DocumentIngestionService> _logger;

    public DocumentIngestionService(
        AppDbContext db,
        IEmbeddingService embeddings,
        IOptions<KnowledgeOptions> options,
        IHostEnvironment environment,
        ILogger<DocumentIngestionService> logger)
    {
        _db = db;
        _embeddings = embeddings;
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task<IngestionResult> IngestAsync(CancellationToken cancellationToken = default)
    {
        var folder = Path.IsPathRooted(_options.Path) ? _options.Path : Path.Combine(_environment.ContentRootPath, _options.Path);
        if (!Directory.Exists(folder))
        {
            _logger.LogError("Knowledge folder {Folder} does not exist", folder);
            throw new NotFoundException("Không tìm thấy thư mục tài liệu tri thức.");
        }

        var files = Directory.EnumerateFiles(folder)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sources = new List<string>();
        var totalChunks = 0;
        foreach (var file in files)
        {
            var text = await File.ReadAllTextAsync(file, cancellationToken);
            var title = ReadTitle(text, file);
            var pieces = TextChunker.Split(text, _options.ChunkSize, _options.ChunkOverlap);
            if (pieces.Count == 0)
            {
                continue;
            }

            // The title is part of what is embedded, so "bảo hành máy giặt ABC" also matches a chunk that never repeats the product name
            var vectors = await _embeddings.EmbedManyAsync(pieces.Select(p => $"{title}\n{p}").ToList(), EmbeddingInputType.Document, cancellationToken);

            // Embedding comes first: if the provider fails, the old chunks of this document stay untouched
            var old = await _db.KnowledgeChunks.Where(c => c.SourceTitle == title).ToListAsync(cancellationToken);
            _db.KnowledgeChunks.RemoveRange(old);
            var now = DateTime.UtcNow;
            _db.KnowledgeChunks.AddRange(pieces.Select((p, i) => new KnowledgeChunk
            {
                SourceTitle = title,
                Content = p,
                Embedding = VectorMath.ToBytes(vectors[i]),
                CreatedAt = now
            }));
            await _db.SaveChangesAsync(cancellationToken);

            sources.Add(title);
            totalChunks += pieces.Count;
            _logger.LogInformation("Ingested {Title}: {Chunks} chunks", title, pieces.Count);
        }

        return new IngestionResult(sources.Count, totalChunks, sources);
    }

    // The first "# heading" of the file, else the file name without extension
    private static string ReadTitle(string text, string file)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal) && trimmed.Length > 2)
            {
                return trimmed[2..].Trim();
            }
        }

        return Path.GetFileNameWithoutExtension(file);
    }
}
