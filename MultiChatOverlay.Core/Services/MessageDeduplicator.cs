using MultiChatOverlay.Core.Models;

namespace MultiChatOverlay.Core.Services;

public sealed class MessageDeduplicator
{
    private readonly int _capacity;
    private readonly TimeSpan _retention;
    private readonly Queue<(string Key, DateTimeOffset SeenAt)> _order = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public MessageDeduplicator(int capacity = 5000, TimeSpan? retention = null)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
        _retention = retention ?? TimeSpan.FromMinutes(10);
    }

    public bool ShouldAccept(ChatMessage message, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        var key = $"{message.Platform}:{message.Id}";
        var instant = now ?? DateTimeOffset.UtcNow;

        lock (_gate)
        {
            Prune(instant);
            if (!_ids.Add(key))
                return false;

            _order.Enqueue((key, instant));
            while (_order.Count > _capacity)
                _ids.Remove(_order.Dequeue().Key);

            return true;
        }
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
}
