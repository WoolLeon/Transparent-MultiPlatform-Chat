using MultiChatOverlay.Core.Models;
using MultiChatOverlay.Core.Parsing;

namespace MultiChatOverlay.Tests;

public sealed class TwitchIrcParserTests
{
    [Fact]
    public void ParsePrivmsg_ParsesTagsBadgesAndMessage()
    {
        const string line = "@badges=moderator/1,subscriber/12;color=#1E90FF;display-name=Test\\sUser;id=abc;mod=1;subscriber=1;tmi-sent-ts=1700000000000 :test!test@test.tmi.twitch.tv PRIVMSG #channel :hello world";

        var parsed = TwitchIrcParser.ParsePrivmsg(line);

        Assert.NotNull(parsed);
        Assert.Equal("abc", parsed.Id);
        Assert.Equal("test", parsed.Username);
        Assert.Equal("Test User", parsed.DisplayName);
        Assert.Equal("#1E90FF", parsed.UserColor);
        Assert.True(parsed.IsModerator);
        Assert.True(parsed.IsSubscriber);
        Assert.Contains(parsed.Badges, badge => badge.Name == "moderator" && badge.Version == "1");
        Assert.Equal("hello world", Assert.IsType<TextPart>(Assert.Single(parsed.MessageParts)).Text);
    }

    [Fact]
    public void ParsePrivmsg_RecognizesBroadcasterBadge()
    {
        const string line = "@badges=broadcaster/1 :streamer!streamer@streamer.tmi.twitch.tv PRIVMSG #streamer :welcome";

        var parsed = TwitchIrcParser.ParsePrivmsg(line);

        Assert.NotNull(parsed);
        Assert.True(parsed.IsOwner);
        Assert.Contains(parsed.Badges, badge => badge.Name == "broadcaster");
    }

    [Theory]
    [InlineData("sub", "sub")]
    [InlineData("resub", "resub")]
    [InlineData("subgift", "giftsub")]
    [InlineData("anonsubgift", "giftsub")]
    [InlineData("raid", "raid")]
    public void ParseUserNotice_MapsSupportedTwitchAlerts(string messageId, string expectedType)
    {
        var line = $"@display-name=TestUser;id=alert-1;msg-id={messageId};msg-param-cumulative-months=6;msg-param-viewerCount=24;tmi-sent-ts=1700000000000 :test!test@test.tmi.twitch.tv USERNOTICE #channel :hello";

        var parsed = TwitchIrcParser.ParseUserNotice(line);

        Assert.NotNull(parsed);
        Assert.Equal(expectedType, parsed.Type);
        Assert.Equal("TestUser", parsed.User);
        Assert.Equal("alert-1", parsed.Id);
        Assert.Equal("hello", parsed.Message);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000), parsed.Timestamp);
    }

    [Fact]
    public void ParseItems_CheerEmitsAlertAndRegularChatMessage()
    {
        const string line = "@bits=250;display-name=TestUser;id=cheer-1;tmi-sent-ts=1700000000000 :test!test@test.tmi.twitch.tv PRIVMSG #channel :Cheer!";

        var items = TwitchIrcParser.ParseItems(line);

        Assert.Collection(items,
            item =>
            {
                Assert.Equal("cheer", item.Event?.Type);
                Assert.Equal("250", item.Event?.Amount);
                Assert.Equal("Cheer!", item.Event?.Message);
            },
            item => Assert.Equal("cheer-1", item.Message?.Id));
    }

    [Fact]
    public void ParseUserNotice_UsesSenderLoginWhenDisplayNameIsMissing()
    {
        const string line = "@id=alert-2;msg-id=raid;msg-param-viewerCount=24 :raider!raider@raider.tmi.twitch.tv USERNOTICE #channel";

        var parsed = TwitchIrcParser.ParseUserNotice(line);

        Assert.NotNull(parsed);
        Assert.Equal("raider", parsed.User);
        Assert.Equal("24", parsed.Amount);
    }

    [Fact]
    public void ParseUserNotice_IgnoresUnsupportedAlertKinds()
    {
        const string line = "@msg-id=announcement :streamer!streamer@streamer.tmi.twitch.tv USERNOTICE #channel :hello";

        Assert.Null(TwitchIrcParser.ParseUserNotice(line));
    }

    [Fact]
    public void ParsePrivmsg_ConvertsEmoteRangesAndPreservesText()
    {
        const string line = "@display-name=Viewer;emotes=25:0-4,6-10;id=emote-message :viewer!viewer@viewer.tmi.twitch.tv PRIVMSG #channel :Kappa Kappa!";

        var parsed = TwitchIrcParser.ParsePrivmsg(line);

        Assert.NotNull(parsed);
        Assert.Collection(parsed.MessageParts,
            part => Assert.IsType<EmotePart>(part),
            part => Assert.IsType<TextPart>(part),
            part => Assert.IsType<EmotePart>(part),
            part => Assert.Equal("!", Assert.IsType<TextPart>(part).Text));
        Assert.Equal("Kappa", Assert.IsType<EmotePart>(parsed.MessageParts[0]).Name);
        Assert.Equal("https://static-cdn.jtvnw.net/emoticons/v2/25/default/dark/1.0",
            Assert.IsType<EmotePart>(parsed.MessageParts[0]).Url);
    }

    [Fact]
    public void ParseTags_UnescapesEscapedValues()
    {
        var tags = TwitchIrcParser.ParseTags("display-name=A\\sB;custom=one\\:two\\\\three");

        Assert.Equal("A B", tags["display-name"]);
        Assert.Equal("one;two\\three", tags["custom"]);
    }

    [Theory]
    [InlineData(":tmi.twitch.tv NOTICE * :Login authentication failed", true)]
    [InlineData(":tmi.twitch.tv NOTICE * :Improperly formatted auth", true)]
    [InlineData(":tmi.twitch.tv NOTICE #channel :That channel does not exist", false)]
    [InlineData("PING :server", false)]
    public void IsAuthenticationFailureNotice_RecognizesOnlyAuthenticationFailures(string line, bool expected)
    {
        Assert.Equal(expected, TwitchIrcParser.IsAuthenticationFailureNotice(line));
    }

    [Fact]
    public void ParsePrivmsg_IgnoresInvalidEmoteRangesAndNonPrivmsgLines()
    {
        const string line = "@emotes=25:0-999,garbage :viewer!viewer@host PRIVMSG #channel :hey";

        var parsed = TwitchIrcParser.ParsePrivmsg(line);

        Assert.NotNull(parsed);
        Assert.Equal("hey", Assert.IsType<TextPart>(Assert.Single(parsed.MessageParts)).Text);
        Assert.Null(TwitchIrcParser.ParsePrivmsg("PING :server"));
    }
}
