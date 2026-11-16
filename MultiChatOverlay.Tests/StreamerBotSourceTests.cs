using System.Text.Json;
using MultiChatOverlay.Core.Models;
using MultiChatOverlay.Core.Sources;

namespace MultiChatOverlay.Tests;

public sealed class StreamerBotSourceTests
{
    [Fact]
    public void ParseRewardRedemption_MapsRewardUserAndInput()
    {
        using var document = JsonDocument.Parse(
            """{"id":"redeem-123","createdAt":"2026-11-15T10:00:00Z","user":{"name":"Viewer","login":"viewer"},"reward":{"title":"Hydrate","cost":500},"userInput":"Water time"}""");

        var parsed = StreamerBotSource.ParseRewardRedemption(document.RootElement);

        Assert.Equal("redeem-123", parsed.Id);
        Assert.Equal("redemption", parsed.Type);
        Assert.Equal(ChatPlatform.Twitch, parsed.Platform);
        Assert.Equal("Viewer", parsed.User);
        Assert.Equal("Hydrate · 500 points", parsed.Amount);
        Assert.Equal("Water time", parsed.Message);
        Assert.Equal(DateTimeOffset.Parse("2026-11-15T10:00:00Z"), parsed.Timestamp);
    }

    [Fact]
    public void ParseRewardRedemption_SupportsTopLevelTitleAndUsername()
    {
        using var document = JsonDocument.Parse(
            """{"eventId":"redeem-456","title":"Highlight My Message","userName":"viewer"}""");

        var parsed = StreamerBotSource.ParseRewardRedemption(document.RootElement);

        Assert.Equal("redeem-456", parsed.Id);
        Assert.Equal("viewer", parsed.User);
        Assert.Equal("Highlight My Message", parsed.Amount);
        Assert.Null(parsed.Message);
    }
}
