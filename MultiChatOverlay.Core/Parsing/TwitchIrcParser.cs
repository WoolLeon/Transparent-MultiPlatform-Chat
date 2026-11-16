using System.Globalization;
using System.Text;
using MultiChatOverlay.Core.Models;

namespace MultiChatOverlay.Core.Parsing;

public static class TwitchIrcParser
{
    public static bool IsAuthenticationFailureNotice(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var noticeIndex = line.IndexOf(" NOTICE ", StringComparison.Ordinal);
        var messageIndex = noticeIndex < 0
            ? -1
            : line.IndexOf(" :", noticeIndex, StringComparison.Ordinal);
        if (messageIndex < 0)
            return false;

        var message = line[(messageIndex + 2)..];
        return message.Contains("login authentication failed", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("improperly formatted auth", StringComparison.OrdinalIgnoreCase);
    }

    public static ChatMessage? ParsePrivmsg(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var index = 0;
        IReadOnlyDictionary<string, string> tags = new Dictionary<string, string>();
        if (line[index] == '@')
        {
            var end = line.IndexOf(' ');
            if (end < 0)
                return null;

            tags = ParseTags(line.AsSpan(1, end - 1));
            index = end + 1;
        }

        string? prefix = null;
        if (index < line.Length && line[index] == ':')
        {
            var end = line.IndexOf(' ', index);
            if (end < 0)
                return null;

            prefix = line[(index + 1)..end];
            index = end + 1;
        }

        if (!line.AsSpan(index).StartsWith("PRIVMSG ", StringComparison.Ordinal))
            return null;

        var payloadIndex = line.IndexOf(" :", index, StringComparison.Ordinal);
        if (payloadIndex < 0)
            return null;

        var nick = prefix?.Split('!', 2)[0];
        if (string.IsNullOrWhiteSpace(nick))
            return null;

        var messageText = line[(payloadIndex + 2)..];
        var badges = ParseBadges(tags.GetValueOrDefault("badges"));
        var emotes = ParseEmoteRanges(messageText, tags.GetValueOrDefault("emotes"));
        var color = tags.GetValueOrDefault("color");
        if (string.IsNullOrWhiteSpace(color))
            color = null;

        return new ChatMessage
        {
            Id = tags.GetValueOrDefault("id") ?? $"{nick}:{messageText}:{tags.GetValueOrDefault("tmi-sent-ts")}",
            Platform = ChatPlatform.Twitch,
            Username = nick,
            DisplayName = tags.GetValueOrDefault("display-name") is { Length: > 0 } displayName ? displayName : nick,
            UserColor = color,
            Badges = badges,
            MessageParts = BuildParts(messageText, emotes),
            Timestamp = ParseTimestamp(tags.GetValueOrDefault("tmi-sent-ts")),
            IsModerator = tags.GetValueOrDefault("mod") == "1" || badges.Any(b => b.Name == "moderator"),
            IsSubscriber = tags.GetValueOrDefault("subscriber") == "1" || badges.Any(b => b.Name == "subscriber"),
            IsOwner = badges.Any(b => b.Name == "broadcaster"),
            IsHighlighted = tags.GetValueOrDefault("msg-id") == "highlighted-message"
        };
    }

    public static IReadOnlyList<ChatItem> ParseItems(string line)
    {
        var chatEvent = ParseUserNotice(line);
        if (chatEvent is not null)
            return [ChatItem.FromEvent(chatEvent)];

        var message = ParsePrivmsg(line);
        if (message is null)
            return [];

        var items = new List<ChatItem>(2);
        var tags = ParseLineTags(line);
        if (int.TryParse(tags.GetValueOrDefault("bits"), NumberStyles.None, CultureInfo.InvariantCulture,
                out var bits) && bits > 0)
        {
            items.Add(ChatItem.FromEvent(new ChatEvent
            {
                Id = $"{message.Id}:cheer",
                Type = "cheer",
                Platform = ChatPlatform.Twitch,
                User = message.DisplayName,
                Amount = bits.ToString(CultureInfo.InvariantCulture),
                Message = string.Concat(message.MessageParts.Select(part =>
                    part is TextPart text ? text.Text : part is EmotePart emote ? emote.Name : string.Empty)),
                Timestamp = message.Timestamp
            }));
        }

        items.Add(ChatItem.FromMessage(message));
        return items;
    }

    public static ChatEvent? ParseUserNotice(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var commandIndex = line.IndexOf(" USERNOTICE ", StringComparison.Ordinal);
        if (commandIndex < 0)
            return null;

        var tags = ParseLineTags(line);
        var type = tags.GetValueOrDefault("msg-id") switch
        {
            "sub" => "sub",
            "resub" => "resub",
            "subgift" or "anonsubgift" or "submysterygift" or "anonsubmysterygift" => "giftsub",
            "raid" => "raid",
            "ritual" => "custom",
            _ => null
        };
        if (type is null)
            return null;

        var messageStart = line.IndexOf(" :", commandIndex, StringComparison.Ordinal);
        var message = messageStart < 0 ? null : line[(messageStart + 2)..];
        var loginEnd = line.IndexOf('!');
        var loginStart = loginEnd < 0 ? -1 : line.LastIndexOf(' ', loginEnd) + 1;
        var login = loginStart >= 0 && loginEnd > loginStart
            ? line[loginStart..loginEnd].TrimStart(':')
            : null;
        var user = tags.GetValueOrDefault("display-name");
        if (string.IsNullOrWhiteSpace(user))
            user = login;
        if (string.IsNullOrWhiteSpace(user))
            user = tags.GetValueOrDefault("msg-param-recipient-display-name") ?? "Twitch";

        var amount = type switch
        {
            "raid" => tags.GetValueOrDefault("msg-param-viewerCount"),
            "giftsub" => tags.GetValueOrDefault("msg-param-mass-gift-count") ??
                         tags.GetValueOrDefault("msg-param-sender-count") ??
                         tags.GetValueOrDefault("msg-param-months"),
            "resub" => tags.GetValueOrDefault("msg-param-cumulative-months") ??
                       tags.GetValueOrDefault("msg-param-months"),
            _ => tags.GetValueOrDefault("msg-param-months")
        };
        var id = tags.GetValueOrDefault("id");
        if (string.IsNullOrWhiteSpace(id))
            id = $"twitch:{tags.GetValueOrDefault("msg-id")}:{user}:{tags.GetValueOrDefault("tmi-sent-ts")}";

        return new ChatEvent
        {
            Id = id,
            Type = type,
            Platform = ChatPlatform.Twitch,
            User = user,
            Amount = amount,
            Message = string.IsNullOrWhiteSpace(message) ? null : message,
            Timestamp = ParseTimestamp(tags.GetValueOrDefault("tmi-sent-ts"))
        };
    }

    public static IReadOnlyDictionary<string, string> ParseTags(ReadOnlySpan<char> tagText)
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in tagText.ToString().Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = tag.IndexOf('=');
            var key = equals < 0 ? tag : tag[..equals];
            var value = equals < 0 ? string.Empty : UnescapeTag(tag[(equals + 1)..]);
            tags[key] = value;
        }

        return tags;
    }

    private static IReadOnlyDictionary<string, string> ParseLineTags(string line)
    {
        if (line.Length == 0 || line[0] != '@')
            return new Dictionary<string, string>();

        var end = line.IndexOf(' ');
        return end < 0
            ? new Dictionary<string, string>()
            : ParseTags(line.AsSpan(1, end - 1));
    }

    private static string UnescapeTag(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                builder.Append(value[i]);
                continue;
            }

            builder.Append(value[++i] switch
            {
                's' => ' ',
                ':' => ';',
                '\\' => '\\',
                'r' => '\r',
                'n' => '\n',
                var c => c
            });
        }

        return builder.ToString();
    }

    private static IReadOnlyList<ChatBadge> ParseBadges(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return [];

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(badge =>
            {
                var parts = badge.Split('/', 2);
                return new ChatBadge(parts[0], parts.Length == 2 ? parts[1] : string.Empty);
            })
            .ToArray();
    }

    private static IReadOnlyList<(int Start, int End, string Id)> ParseEmoteRanges(string text, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var ranges = new List<(int Start, int End, string Id)>();
        foreach (var emote in value.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = emote.Split(':', 2);
            if (parts.Length != 2)
                continue;

            foreach (var range in parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var bounds = range.Split('-', 2);
                if (bounds.Length != 2 ||
                    !int.TryParse(bounds[0], NumberStyles.None, CultureInfo.InvariantCulture, out var start) ||
                    !int.TryParse(bounds[1], NumberStyles.None, CultureInfo.InvariantCulture, out var end) ||
                    start < 0 || end < start || end >= text.Length)
                    continue;

                ranges.Add((start, end, parts[0]));
            }
        }

        ranges.Sort((left, right) => left.Start.CompareTo(right.Start));
        var accepted = new List<(int Start, int End, string Id)>();
        var previousEnd = -1;
        foreach (var range in ranges)
        {
            if (range.Start <= previousEnd)
                continue;
            accepted.Add(range);
            previousEnd = range.End;
        }

        return accepted;
    }

    private static IReadOnlyList<ChatPart> BuildParts(string text, IReadOnlyList<(int Start, int End, string Id)> ranges)
    {
        var parts = new List<ChatPart>();
        var cursor = 0;
        foreach (var range in ranges)
        {
            if (range.Start > cursor)
                parts.Add(new TextPart(text[cursor..range.Start]));

            var name = text[range.Start..(range.End + 1)];
            parts.Add(new EmotePart(name, $"https://static-cdn.jtvnw.net/emoticons/v2/{Uri.EscapeDataString(range.Id)}/default/dark/1.0", "twitch"));
            cursor = range.End + 1;
        }

        if (cursor < text.Length)
            parts.Add(new TextPart(text[cursor..]));

        if (parts.Count == 0)
            parts.Add(new TextPart(text));

        return parts;
    }

    private static DateTimeOffset ParseTimestamp(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : DateTimeOffset.UtcNow;
}
