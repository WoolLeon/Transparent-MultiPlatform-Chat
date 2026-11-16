namespace MultiChatOverlay.Core.Models;

public sealed record ChatMessage
{
    public required string Id { get; init; }
    public required ChatPlatform Platform { get; init; }
    public required string Username { get; init; }
    public required string DisplayName { get; init; }
    public string? UserColor { get; init; }
    public IReadOnlyList<ChatBadge> Badges { get; init; } = [];
    public IReadOnlyList<ChatPart> MessageParts { get; init; } = [];
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public bool IsHighlighted { get; init; }
    public bool IsModerator { get; init; }
    public bool IsSubscriber { get; init; }
    public bool IsOwner { get; init; }
}
