using System.Text.Json;
using MultiChatOverlay.Core.Models;
using MultiChatOverlay.Core.Sources;

namespace MultiChatOverlay.Tests;

public sealed class TwitchEventSubSourceTests
{
    [Fact]
    public void ParseRedemption_MapsCustomRewardPayload()
    {
        using var document = JsonDocument.Parse(
            """{"id":"redemption-1","user_name":"Viewer","user_login":"viewer","user_input":"Hydrate!","redeemed_at":"2026-11-15T10:00:00Z","reward":{"title":"Hydrate","cost":500}}""");

        var item = TwitchEventSubSource.ParseRedemption(
            "channel.channel_points_custom_reward_redemption.add", document.RootElement);

        Assert.NotNull(item?.Event);
        Assert.Equal("redemption-1", item.Event.Id);
        Assert.Equal("redemption", item.Event.Type);
        Assert.Equal(ChatPlatform.Twitch, item.Event.Platform);
        Assert.Equal("Viewer", item.Event.User);
        Assert.Equal("Hydrate · 500 points", item.Event.Amount);
        Assert.Equal("Hydrate!", item.Event.Message);
        Assert.Equal(DateTimeOffset.Parse("2026-11-15T10:00:00Z"), item.Event.Timestamp);
    }

    [Fact]
    public void ParseRedemption_MapsAutomaticRewardPayload()
    {
        using var document = JsonDocument.Parse(
            """{"id":"redemption-2","user_login":"viewer","reward":{"type":"message_effect","cost":100}}""");

        var item = TwitchEventSubSource.ParseRedemption(
            "channel.channel_points_automatic_reward_redemption.add", document.RootElement);

        Assert.NotNull(item?.Event);
        Assert.Equal("viewer", item.Event.User);
        Assert.Equal("message_effect · 100 points", item.Event.Amount);
    }

    [Fact]
    public void ParseRedemption_IgnoresOtherEventTypes()
    {
        using var document = JsonDocument.Parse("""{"id":"event-1"}""");

        Assert.Null(TwitchEventSubSource.ParseRedemption("channel.follow", document.RootElement));
    }
}
