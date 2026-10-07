using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Domain.Entities;

// One passage of a knowledge document (warranty policy, return policy, product guide) with its embedding vector,
// used by the assistant to answer from real documents (RAG)
public class KnowledgeChunk
{
    [Key]
    public int Id { get; set; }

    // Name of the document the passage came from; shown to the customer as the source
    [Required]
    [MaxLength(300)]
    public string SourceTitle { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;

    // The vector as little-endian float32 values (4 bytes each); see VectorMath
    [Required]
    public byte[] Embedding { get; set; } = Array.Empty<byte>();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
