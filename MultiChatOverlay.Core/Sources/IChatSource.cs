using MultiChatOverlay.Core.Models;

namespace MultiChatOverlay.Core.Sources;

public interface IChatSource : IAsyncDisposable
{
    ChatPlatform Platform { get; }
    ConnectionState State { get; }
    event Action<ChatItem>? ItemReceived;
    event Action<ConnectionState>? StateChanged;
    event Action<Exception>? ErrorOccurred;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
