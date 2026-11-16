using MultiChatOverlay.Core.Models;
using MultiChatOverlay.Core.Services;

namespace MultiChatOverlay.Tests;

public sealed class MessageDeduplicatorTests
{
    [Fact]
    public void ShouldAccept_RejectsRepeatedPlatformMessageId()
    {
        var deduplicator = new MessageDeduplicator();
        var message = CreateMessage("same-id");

        Assert.True(deduplicator.ShouldAccept(message));
        Assert.False(deduplicator.ShouldAccept(message));
    }

    [Fact]
    public void ShouldAccept_ScopesIdsToPlatform()
    {
        var deduplicator = new MessageDeduplicator();

        Assert.True(deduplicator.ShouldAccept(CreateMessage("id", ChatPlatform.Twitch)));
        Assert.True(deduplicator.ShouldAccept(CreateMessage("id", ChatPlatform.YouTube)));
    }

    [Fact]
    public void ShouldAccept_ExpiresOldIdsAndBoundsStorage()
    {
        var now = DateTimeOffset.UtcNow;
        var deduplicator = new MessageDeduplicator(capacity: 2, retention: TimeSpan.FromMinutes(1));
        Assert.True(deduplicator.ShouldAccept(CreateMessage("old"), now));
        Assert.True(deduplicator.ShouldAccept(CreateMessage("middle"), now));
        Assert.True(deduplicator.ShouldAccept(CreateMessage("new"), now));
        Assert.True(deduplicator.ShouldAccept(CreateMessage("old"), now.AddMinutes(2)));
    }

    private static ChatMessage CreateMessage(string id, ChatPlatform platform = ChatPlatform.Twitch) => new()
    {
        Id = id,
        Platform = platform,
        Username = "viewer",
        DisplayName = "Viewer",
        MessageParts = [new TextPart("hello")]
    };
}
