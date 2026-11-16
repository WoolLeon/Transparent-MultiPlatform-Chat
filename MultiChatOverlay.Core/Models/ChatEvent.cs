namespace MultiChatOverlay.Core.Models;

public sealed record ChatEvent
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required ChatPlatform Platform { get; init; }
    public required string User { get; init; }
    public string? Amount { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
