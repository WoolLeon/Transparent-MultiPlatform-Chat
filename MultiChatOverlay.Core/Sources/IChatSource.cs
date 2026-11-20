using MultiChatOverlay.Core.Models;

namespace MultiChatOverlay.Core.Sources;

public interface IChatSource : IAsyncDisposable
{
    ChatPlatform Platform { get; }
    ConnectionState State { get; }
    ConnectionHealth Health { get; }
    event Action<ChatItem>? ItemReceived;
    event Action<ConnectionState>? StateChanged;
    event Action<Exception>? ErrorOccurred;
    event Action<ConnectionHealth>? HealthChanged;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Optionally adjust polling interval at runtime (e.g., for Facebook source)
    /// </summary>
    void SetPollInterval(TimeSpan interval);
}
