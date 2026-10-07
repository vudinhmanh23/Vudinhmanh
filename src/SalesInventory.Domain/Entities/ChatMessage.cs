using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Domain.Entities;

// One turn of a conversation: what the user asked or what the assistant answered
public class ChatMessage
{
    [Key]
    public int Id { get; set; }

    public Guid ConversationId { get; set; }

    public Conversation Conversation { get; set; } = null!;

    // "user" or "assistant" (the role names the Messages API uses)
    [Required]
    [MaxLength(20)]
    public string Role { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
