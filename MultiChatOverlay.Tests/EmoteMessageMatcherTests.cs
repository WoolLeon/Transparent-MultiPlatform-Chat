using MultiChatOverlay.Core.Models;
using MultiChatOverlay.Core.Services;

namespace MultiChatOverlay.Tests;

public sealed class EmoteMessageMatcherTests
{
    [Fact]
    public void Apply_MatchesEmotesNextToPunctuationAndPreservesText()
    {
        var matcher = new EmoteMessageMatcher();
        matcher.Add(new EmotePart("catDisco", "https://cdn.example/catDisco.webp", "7tv"));
        var message = CreateMessage("Nice, catDisco! catDiscoParty");

        var result = matcher.Apply(message);

        Assert.Collection(result.MessageParts,
            part => Assert.Equal("Nice, ", Assert.IsType<TextPart>(part).Text),
            part => Assert.Equal("catDisco", Assert.IsType<EmotePart>(part).Name),
            part => Assert.Equal("! catDiscoParty", Assert.IsType<TextPart>(part).Text));
    }

    [Fact]
    public void Add_ChannelSetEmoteOverridesGlobalSetWithSameName()
    {
        var matcher = new EmoteMessageMatcher();
        matcher.Add(new EmotePart("muted", "https://cdn.example/global.webp", "7tv"));
        matcher.Add(new EmotePart("muted", "https://cdn.example/channel.webp", "7tv"), replaceExisting: true);

        var result = matcher.Apply(CreateMessage("muted"));
        var emote = Assert.IsType<EmotePart>(Assert.Single(result.MessageParts));

        Assert.Equal("https://cdn.example/channel.webp", emote.Url);
    }

    private static ChatMessage CreateMessage(string text) => new()
    {
        Id = "test",
        Platform = ChatPlatform.Twitch,
        Username = "viewer",
        DisplayName = "Viewer",
        MessageParts = [new TextPart(text)]
    };
}
