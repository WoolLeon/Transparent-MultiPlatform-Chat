using MultiChatOverlay.Core.Models;
using MultiChatOverlay.Core.Services;

namespace MultiChatOverlay.Tests;

public sealed class ChatEventDeduplicatorTests
{
    [Fact]
    public void ShouldAccept_SuppressesMatchingNativeAndStreamerBotEvents()
    {
        var deduplicator = new ChatEventDeduplicator();
        var now = DateTimeOffset.UtcNow;

        Assert.True(deduplicator.ShouldAccept(CreateEvent("native-id", "6", "native message"),
            ChatItemSource.Native, true, now));
        Assert.False(deduplicator.ShouldAccept(CreateEvent("relay-id", null, "different formatting"),
            ChatItemSource.StreamerBotRelay, true, now.AddSeconds(5)));
    }

    [Fact]
    public void ShouldAccept_AllowsMatchingEventsFromTheSameSource()
    {
        var deduplicator = new ChatEventDeduplicator();

        Assert.True(deduplicator.ShouldAccept(CreateEvent("first"), ChatItemSource.Native, true));
        Assert.True(deduplicator.ShouldAccept(CreateEvent("second"), ChatItemSource.Native, true));
    }

    [Fact]
    public void ShouldAccept_StillRejectsRepeatedIdsWhenCrossSourceMatchingIsDisabled()
    {
        var deduplicator = new ChatEventDeduplicator();
        var chatEvent = CreateEvent("same-id");

        Assert.True(deduplicator.ShouldAccept(chatEvent, ChatItemSource.Native, false));
        Assert.False(deduplicator.ShouldAccept(chatEvent, ChatItemSource.StreamerBotRelay, false));
    }

    private static ChatEvent CreateEvent(string id, string? amount = null, string? message = "Thanks for the support") => new()
    {
        Id = id,
        Type = "sub",
        Platform = ChatPlatform.Twitch,
        User = "viewer",
        Amount = amount,
        Message = message
    };
}
