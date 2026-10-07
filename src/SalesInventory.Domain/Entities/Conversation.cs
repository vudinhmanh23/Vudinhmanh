using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Domain.Entities;

// One chat between a signed-in user and the assistant. The id is a Guid so a client can pick it before the first message.
public class Conversation
{
    [Key]
    public Guid Id { get; set; }

    // The Identity user who owns the conversation; nobody else can read or continue it
    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    // The first question, shortened; shown in a history list
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Moves forward with every answer, so the newest conversation sorts first
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
