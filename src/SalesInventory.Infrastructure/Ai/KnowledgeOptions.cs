namespace SalesInventory.Infrastructure.Ai;

// Bound from the "Knowledge" section: where the documents live and how they are cut and searched
public class KnowledgeOptions
{
    public const string SectionName = "Knowledge";

    // Folder with the .md/.txt documents; a relative path is resolved against the application's content root
    public string Path { get; set; } = "Knowledge";

    // Target chunk length in characters, and how many characters of the previous chunk the next one repeats
    public int ChunkSize { get; set; } = 500;
    public int ChunkOverlap { get; set; } = 100;

    // Passages given to the model per question
    public int TopK { get; set; } = 3;

    // Cosine similarity below this means "not related": no context is added and the assistant says it does not know
    public double MinScore { get; set; } = 0.3;
}
