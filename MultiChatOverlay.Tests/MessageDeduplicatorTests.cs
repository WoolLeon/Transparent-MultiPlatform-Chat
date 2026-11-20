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

    [Fact]
    public void ShouldAccept_SuppressesMatchingNativeAndStreamerBotMessages()
    {
        var deduplicator = new MessageDeduplicator();
        var now = DateTimeOffset.UtcNow;
        var native = CreateMessage("native-id");
        var relayed = CreateMessage("relay-id");

        Assert.True(deduplicator.ShouldAccept(native, ChatItemSource.Native, true, true, now));
        Assert.False(deduplicator.ShouldAccept(relayed, ChatItemSource.StreamerBotRelay, true, true,
            now.AddSeconds(2)));
    }

    [Fact]
    public void ShouldAccept_AllowsMatchingMessagesFromTheSameSource()
    {
        var deduplicator = new MessageDeduplicator();
        var first = CreateMessage("first");
        var second = CreateMessage("second");

        Assert.True(deduplicator.ShouldAccept(first, ChatItemSource.Native, true, true));
        Assert.True(deduplicator.ShouldAccept(second, ChatItemSource.Native, true, true));
    }

    [Fact]
    public void ShouldAccept_RespectsCrossSourceSettingWhenIdDeduplicationIsDisabled()
    {
        var deduplicator = new MessageDeduplicator();
        var now = DateTimeOffset.UtcNow;

        Assert.True(deduplicator.ShouldAccept(CreateMessage("native-id"), ChatItemSource.Native,
            false, false, now));
        Assert.True(deduplicator.ShouldAccept(CreateMessage("relay-id"), ChatItemSource.StreamerBotRelay,
            false, false, now.AddSeconds(1)));
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
