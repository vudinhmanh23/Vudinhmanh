using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Exceptions;
using SalesInventory.Application.Interfaces;
using SalesInventory.Domain.Entities;
using SalesInventory.Infrastructure.Persistence;

namespace SalesInventory.Infrastructure.Ai;

public sealed class ConversationStore : IConversationStore
{
    private const int MaxTitleLength = 80;

    private readonly AppDbContext _db;
    private readonly AiSafetyOptions _safety;

    public ConversationStore(AppDbContext db, IOptions<AiSafetyOptions> safety)
    {
        _db = db;
        _safety = safety.Value;
    }

    public async Task<IReadOnlyList<ChatTurn>> GetHistoryAsync(Guid conversationId, string userId, CancellationToken cancellationToken = default)
    {
        var owner = await FindOwnerAsync(conversationId, cancellationToken);
        if (owner is null)
        {
            return Array.Empty<ChatTurn>();
        }

        if (owner != userId)
        {
            throw NotFound();
        }

        var limit = Math.Max(0, _safety.MaxHistoryMessages);
        var latest = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.Id)
            .Take(limit)
            .Select(m => new ChatTurn(m.Role, m.Content))
            .ToListAsync(cancellationToken);

        latest.Reverse();

        // The model must see the conversation start with the user's turn
        while (latest.Count > 0 && latest[0].Role != "user")
        {
            latest.RemoveAt(0);
        }

        return latest;
    }

    public async Task AppendUserAsync(Guid conversationId, string userId, string message, CancellationToken cancellationToken = default)
    {
        var conversation = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            conversation = new Conversation
            {
                Id = conversationId,
                UserId = userId,
                Title = message.Length <= MaxTitleLength ? message : message[..MaxTitleLength].TrimEnd() + "…"
            };
            _db.Conversations.Add(conversation);
        }
        else if (conversation.UserId != userId)
        {
            throw NotFound();
        }

        await AddMessageAsync(conversation, "user", message, cancellationToken);
    }

    public async Task AppendAssistantAsync(Guid conversationId, string userId, string answer, CancellationToken cancellationToken = default)
    {
        var conversation = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, cancellationToken)
            ?? throw NotFound();

        await AddMessageAsync(conversation, "assistant", answer, cancellationToken);
    }

    public async Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(Guid conversationId, string userId, CancellationToken cancellationToken = default)
    {
        var owner = await FindOwnerAsync(conversationId, cancellationToken);
        if (owner != userId)
        {
            // Missing and someone else's look the same, so ids cannot be probed
            throw NotFound();
        }

        return await _db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .Select(m => new ChatMessageDto { Id = m.Id, Role = m.Role, Content = m.Content, CreatedAt = m.CreatedAt })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationSummaryDto>> ListAsync(string userId, int take, CancellationToken cancellationToken = default)
    {
        return await _db.Conversations.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAt)
            .Take(Math.Clamp(take, 1, 100))
            .Select(c => new ConversationSummaryDto { Id = c.Id, Title = c.Title, UpdatedAt = c.UpdatedAt })
            .ToListAsync(cancellationToken);
    }

    private Task<string?> FindOwnerAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        return _db.Conversations.AsNoTracking()
            .Where(c => c.Id == conversationId)
            .Select(c => c.UserId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task AddMessageAsync(Conversation conversation, string role, string content, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        conversation.UpdatedAt = now;
        _db.ChatMessages.Add(new ChatMessage { Conversation = conversation, Role = role, Content = content, CreatedAt = now });
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static NotFoundException NotFound() => new("Không tìm thấy cuộc trò chuyện.");
}
