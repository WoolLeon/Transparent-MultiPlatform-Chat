using MultiChatOverlay.Core.Models;

namespace MultiChatOverlay.Core.Services;

public sealed class ChatEventDeduplicator
{
    private static readonly TimeSpan CrossSourceWindow = TimeSpan.FromSeconds(15);
    private readonly int _capacity;
    private readonly Queue<(string Key, DateTimeOffset SeenAt)> _idOrder = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<SourceFingerprint>> _fingerprints = new(StringComparer.Ordinal);
    private readonly Queue<SourceFingerprint> _fingerprintOrder = new();
    private readonly object _gate = new();

    public ChatEventDeduplicator(int capacity = 2000)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
    }

    public bool ShouldAccept(
        ChatEvent chatEvent,
        ChatItemSource source,
        bool deduplicateRelayedSources,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(chatEvent);
        var instant = now ?? DateTimeOffset.UtcNow;
        var idKey = $"{chatEvent.Platform}:{chatEvent.Id}";
        var (fingerprint, amount) = CreateFingerprint(chatEvent);

        lock (_gate)
        {
            if (!_ids.Add(idKey))
                return false;

            _idOrder.Enqueue((idKey, instant));
            while (_idOrder.Count > _capacity)
                _ids.Remove(_idOrder.Dequeue().Key);

            if (!deduplicateRelayedSources)
                return true;

            PruneFingerprints(instant);
            if (_fingerprints.TryGetValue(fingerprint, out var matches) &&
                matches.FirstOrDefault(entry =>
                    !entry.Matched &&
                    entry.Source != source &&
                    (entry.Amount.Length == 0 || amount.Length == 0 ||
                     string.Equals(entry.Amount, amount, StringComparison.Ordinal)) &&
                    Math.Abs((instant - entry.SeenAt).TotalSeconds) <= CrossSourceWindow.TotalSeconds) is { } match)
            {
                match.Matched = true;
                return false;
            }

            AddFingerprint(fingerprint, amount, source, instant);
            return true;
        }
    }

    private static (string Key, string Amount) CreateFingerprint(ChatEvent chatEvent)
    {
        var user = Normalize(chatEvent.User);
        var amount = Normalize(chatEvent.Amount ?? string.Empty);
        return ($"{chatEvent.Platform}:{Normalize(chatEvent.Type)}:{user}", amount);
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();

    private void AddFingerprint(string key, string amount, ChatItemSource source, DateTimeOffset seenAt)
    {
        var entry = new SourceFingerprint(key, amount, source, seenAt);
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

    private sealed class SourceFingerprint(string key, string amount, ChatItemSource source, DateTimeOffset seenAt)
    {
        public string Key { get; } = key;
        public string Amount { get; } = amount;
        public ChatItemSource Source { get; } = source;
        public DateTimeOffset SeenAt { get; } = seenAt;
        public bool Matched { get; set; }
    }
}
