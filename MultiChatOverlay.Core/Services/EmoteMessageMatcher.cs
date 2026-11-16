using System.Text.RegularExpressions;
using MultiChatOverlay.Core.Models;

namespace MultiChatOverlay.Core.Services;

public sealed class EmoteMessageMatcher
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private readonly Dictionary<string, EmotePart> _emotes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private Regex? _pattern;

    public void Clear()
    {
        lock (_gate)
        {
            _emotes.Clear();
            _pattern = null;
        }
    }

    public void Add(EmotePart emote, bool replaceExisting = false)
    {
        lock (_gate)
        {
            if (!replaceExisting && _emotes.ContainsKey(emote.Name))
                return;

            _emotes[emote.Name] = emote;
            _pattern = null;
        }
    }

    public int CountForProvider(string provider)
    {
        lock (_gate)
            return _emotes.Values.Count(emote => emote.Provider == provider);
    }

    public ChatMessage Apply(ChatMessage message)
    {
        if (message.Platform is not (ChatPlatform.Twitch or ChatPlatform.StreamerBot))
            return message;

        Regex? pattern;
        lock (_gate)
        {
            if (_emotes.Count == 0)
                return message;
            _pattern ??= BuildPattern();
            pattern = _pattern;
        }

        var parts = new List<ChatPart>();
        foreach (var part in message.MessageParts)
        {
            if (part is not TextPart textPart)
            {
                parts.Add(part);
                continue;
            }

            if (pattern is null)
            {
                parts.Add(textPart);
                continue;
            }

            var cursor = 0;
            foreach (Match match in pattern.Matches(textPart.Text))
            {
                if (match.Index > cursor)
                    parts.Add(new TextPart(textPart.Text[cursor..match.Index]));

                lock (_gate)
                    parts.Add(_emotes.TryGetValue(match.Value, out var emote)
                        ? emote
                        : new TextPart(match.Value));
                cursor = match.Index + match.Length;
            }

            if (cursor < textPart.Text.Length)
                parts.Add(new TextPart(textPart.Text[cursor..]));
        }

        return parts.Count == 0 ? message : message with { MessageParts = parts };
    }

    private Regex? BuildPattern()
    {
        if (_emotes.Count == 0)
            return null;

        var names = _emotes.Keys
            .OrderByDescending(name => name.Length)
            .Select(Regex.Escape);
        var alternatives = string.Join("|", names);
        return new Regex(
            $"(?<![\\p{{L}}\\p{{N}}_])(?:{alternatives})(?![\\p{{L}}\\p{{N}}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            MatchTimeout);
    }
}
