using MultiChatOverlay.Core.Models;

namespace MultiChatOverlay.Core.Services;

public sealed class MessageDeduplicator
{
    private static readonly TimeSpan CrossSourceWindow = TimeSpan.FromSeconds(8);
    private readonly int _capacity;
    private readonly TimeSpan _retention;
    private readonly Queue<(string Key, DateTimeOffset SeenAt)> _order = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<SourceFingerprint>> _fingerprints = new(StringComparer.Ordinal);
    private readonly Queue<SourceFingerprint> _fingerprintOrder = new();
    private readonly object _gate = new();

    public MessageDeduplicator(int capacity = 5000, TimeSpan? retention = null)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
        _retention = retention ?? TimeSpan.FromMinutes(10);
    }

    public bool ShouldAccept(ChatMessage message, DateTimeOffset? now = null)
        => ShouldAccept(message, ChatItemSource.Native, deduplicateMessageIds: true,
            deduplicateRelayedSources: false, now);

    public bool ShouldAccept(
        ChatMessage message,
        ChatItemSource source,
        bool deduplicateMessageIds,
        bool deduplicateRelayedSources,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        var key = $"{message.Platform}:{message.Id}";
        var instant = now ?? DateTimeOffset.UtcNow;

        lock (_gate)
        {
            if (deduplicateMessageIds)
            {
                Prune(instant);
                if (!_ids.Add(key))
                    return false;

                _order.Enqueue((key, instant));
                while (_order.Count > _capacity)
                    _ids.Remove(_order.Dequeue().Key);
            }

            if (deduplicateRelayedSources && TryCreateFingerprint(message, out var fingerprint))
            {
                PruneFingerprints(instant);
                if (_fingerprints.TryGetValue(fingerprint, out var matches) &&
                    matches.FirstOrDefault(entry =>
                        !entry.Matched &&
                        entry.Source != source &&
                        Math.Abs((instant - entry.SeenAt).TotalSeconds) <= CrossSourceWindow.TotalSeconds) is { } match)
                {
                    match.Matched = true;
                    return false;
                }

                AddFingerprint(fingerprint, source, instant);
            }

            return true;
        }
    }

    private static bool TryCreateFingerprint(ChatMessage message, out string fingerprint)
    {
        var username = Normalize(message.Username);
        if (username.Length == 0)
            username = Normalize(message.DisplayName);

        var content = Normalize(string.Concat(message.MessageParts.Select(part => part switch
        {
            TextPart text => text.Text,
            EmotePart emote => emote.Name,
            _ => string.Empty
        })));
        if (username.Length == 0 || content.Length == 0)
        {
            fingerprint = string.Empty;
            return false;
        }

        fingerprint = $"{message.Platform}:{username}:{content}";
        return true;
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();

    private void AddFingerprint(string key, ChatItemSource source, DateTimeOffset seenAt)
    {
        var entry = new SourceFingerprint(key, source, seenAt);
        if (!_fingerprints.TryGetValue(key, out var entries))
        {
            entries = [];
            _fingerprints.Add(key, entries);
        }

        entries.Add(entry);
        _fingerprintOrder.Enqueue(entry);
        while (_fingerprintOrder.Count > _capacity)
            RemoveFingerprint(_fingerprintOrder.Dequeue());
    }

    private void PruneFingerprints(DateTimeOffset now)
    {
        while (_fingerprintOrder.TryPeek(out var oldest) &&
               (now - oldest.SeenAt > CrossSourceWindow || _fingerprintOrder.Count >= _capacity))
        {
            _fingerprintOrder.Dequeue();
            RemoveFingerprint(oldest);
        }
    }

    private void RemoveFingerprint(SourceFingerprint entry)
    {
        if (!_fingerprints.TryGetValue(entry.Key, out var entries))
            return;

        entries.Remove(entry);
        if (entries.Count == 0)
            _fingerprints.Remove(entry.Key);
    }

    private void Prune(DateTimeOffset now)
    {
        while (_order.TryPeek(out var oldest) &&
               (now - oldest.SeenAt > _retention || _order.Count >= _capacity))
        {
            _order.Dequeue();
            _ids.Remove(oldest.Key);
        }
    }

    private sealed class SourceFingerprint(string key, ChatItemSource source, DateTimeOffset seenAt)
    {
        public string Key { get; } = key;
        public ChatItemSource Source { get; } = source;
        public DateTimeOffset SeenAt { get; } = seenAt;
        public bool Matched { get; set; }
    }
}
