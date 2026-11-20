using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MultiChatOverlay.Core.Models;

namespace MultiChatOverlay.Core.Sources;

public sealed class StreamerBotSource(Uri serverUri) : IChatSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Uri _serverUri = serverUri;
    private CancellationTokenSource? _lifetime;
    private Task? _runTask;
    private ClientWebSocket? _activeSocket;
    private ConnectionState _state = ConnectionState.Disconnected;
    private ConnectionHealth _health = ConnectionHealth.Unhealthy(DateTimeOffset.MinValue, 0);

    public ChatPlatform Platform => ChatPlatform.StreamerBot;
    public ConnectionState State => _state;
    public ConnectionHealth Health => _health;
    public event Action<ChatItem>? ItemReceived;
    public event Action<ConnectionState>? StateChanged;
    public event Action<Exception>? ErrorOccurred;
    public event Action<ConnectionHealth>? HealthChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_runTask is not null)
            return Task.CompletedTask;

        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runTask = RunAsync(_lifetime.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_lifetime is null)
            return;

        await _lifetime.CancelAsync();
        if (_activeSocket?.State == WebSocketState.Open)
            await _activeSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);

        if (_runTask is not null)
        {
            try
            {
                await _runTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
        }

        _lifetime.Dispose();
        _lifetime = null;
        _runTask = null;
        SetState(ConnectionState.Disconnected);
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    public async Task SendMessageAsync(string platform, string message, CancellationToken cancellationToken = default)
    {
        if (_activeSocket?.State != WebSocketState.Open)
            throw new InvalidOperationException("Streamer.bot is not connected.");

        await SendRequestAsync(new
        {
            request = "SendMessage",
            id = Guid.NewGuid().ToString("N"),
            platform,
            bot = false,
            @internal = false,
            message
        }, cancellationToken);
    }

    public async Task ExecuteActionAsync(string actionName, CancellationToken cancellationToken = default)
    {
        if (_activeSocket?.State != WebSocketState.Open)
            throw new InvalidOperationException("Streamer.bot is not connected.");

        await SendRequestAsync(new
        {
            request = "DoAction",
            id = Guid.NewGuid().ToString("N"),
            action = new { name = actionName }
        }, cancellationToken);
    }

    public void SetPollInterval(TimeSpan interval)
    {
        // Streamer.bot uses WebSocket, not polling - no-op
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        var firstAttempt = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            _activeSocket = socket;
            try
            {
                SetState(firstAttempt ? ConnectionState.Connecting : ConnectionState.Reconnecting);
                firstAttempt = false;
                var connectStart = DateTimeOffset.UtcNow;
                await socket.ConnectAsync(_serverUri, cancellationToken);
                await SendSubscribeAsync(socket, cancellationToken);
                var latency = DateTimeOffset.UtcNow - connectStart;
                SetState(ConnectionState.Connected);
                SetHealth(ConnectionHealth.Healthy(DateTimeOffset.UtcNow, latency));
                delay = TimeSpan.FromSeconds(1);
                await ReceiveLoopAsync(socket, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ErrorOccurred?.Invoke(ex);
                SetHealth(_health.RecordFailure());
                SetState(ConnectionState.Reconnecting);
            }
            finally
            {
                if (ReferenceEquals(_activeSocket, socket))
                    _activeSocket = null;
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }

    private static Task SendSubscribeAsync(ClientWebSocket socket, CancellationToken cancellationToken) =>
        SendAsync(socket, new
        {
            request = "Subscribe",
            id = Guid.NewGuid().ToString("N"),
            events = new Dictionary<string, string[]>
            {
                ["Twitch"] = ["ChatMessage", "Follow", "Sub", "ReSub", "GiftSub", "Raid", "Cheer", "RewardRedemption"],
                ["YouTube"] = ["Message", "SuperChat", "SuperSticker", "NewSponsor", "MembershipGift"],
                ["Kick"] = ["ChatMessage", "Follow", "Subscription", "GiftSubscription", "MassGiftSubscription", "Resubscription"]
            }
        }, cancellationToken);

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var content = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;
                    content.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                using var document = JsonDocument.Parse(content.ToArray());
                if (document.RootElement.TryGetProperty("event", out var eventInfo) &&
                    document.RootElement.TryGetProperty("data", out var data))
                    PublishEvent(eventInfo, data);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void PublishEvent(JsonElement eventInfo, JsonElement data)
    {
        var source = eventInfo.TryGetProperty("source", out var sourceElement) ? sourceElement.GetString() : null;
        var type = eventInfo.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        var platform = source?.ToLowerInvariant() switch
        {
            "twitch" => ChatPlatform.Twitch,
            "youtube" => ChatPlatform.YouTube,
            "kick" => ChatPlatform.Kick,
            _ => (ChatPlatform?)null
        };
        if (platform is null || string.IsNullOrWhiteSpace(type))
            return;

        if (type is "ChatMessage" or "Message")
        {
            var user = GetNestedString(data, "user", "name") ?? GetNestedString(data, "user", "login") ?? "Unknown";
            var text = GetString(data, "text") ?? GetString(data, "message") ?? string.Empty;
            var id = GetString(data, "messageId") ?? GetString(data, "eventId") ?? $"{source}:{user}:{text}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            ItemReceived?.Invoke(ChatItem.FromMessage(new ChatMessage
            {
                Id = id,
                Platform = platform.Value,
                Username = GetNestedString(data, "user", "login") ?? user,
                DisplayName = user,
                UserColor = GetNestedString(data, "user", "color"),
                MessageParts = [new TextPart(text)],
                IsModerator = GetNestedBoolean(data, "user", "isModerator"),
                IsSubscriber = GetNestedBoolean(data, "user", "subscribed") || GetNestedBoolean(data, "user", "isSponsor"),
                IsOwner = GetNestedBoolean(data, "user", "isOwner"),
                Timestamp = ParseDateTime(GetString(data, "createdAt") ?? GetString(data, "publishedAt"))
            }, ChatItemSource.StreamerBotRelay));
            return;
        }

        if (platform == ChatPlatform.Twitch &&
            type.Equals("RewardRedemption", StringComparison.OrdinalIgnoreCase))
        {
            ItemReceived?.Invoke(ChatItem.FromEvent(ParseRewardRedemption(data), ChatItemSource.StreamerBotRelay));
            return;
        }

        var eventType = type.ToLowerInvariant() switch
        {
            "follow" => "follow",
            "sub" or "subscribe" or "subscription" or "newsubscriber" => "sub",
            "resub" or "resubscribe" or "resubscription" => "resub",
            "giftsub" or "giftmembership" or "membershipgift" or "giftsubscription" => "giftsub",
            "raid" => "raid",
            "cheer" => "cheer",
            "superchat" => "superchat",
            "supersticker" => "superchat",
            _ => "custom"
        };
        var eventUser = GetNestedString(data, "user", "name") ?? GetNestedString(data, "targetUser", "name") ??
                        GetNestedString(data, "raider", "name") ?? "Streamer.bot";
        ItemReceived?.Invoke(ChatItem.FromEvent(new ChatEvent
        {
            Id = GetString(data, "messageId") ?? GetString(data, "eventId") ?? GetString(data, "id") ??
                 Guid.NewGuid().ToString("N"),
            Type = eventType,
            Platform = platform.Value,
            User = eventUser,
            Amount = GetString(data, "amount") ?? GetString(data, "viewers") ?? GetString(data, "bits"),
            Message = GetString(data, "text") ?? GetString(data, "message"),
            Timestamp = ParseDateTime(GetString(data, "createdAt") ?? GetString(data, "publishedAt"))
        }, ChatItemSource.StreamerBotRelay));
    }

    internal static ChatEvent ParseRewardRedemption(JsonElement data)
    {
        var rewardTitle = GetNestedString(data, "reward", "title") ??
                          GetString(data, "title") ??
                          "Channel point reward";
        var rewardCost = GetNestedString(data, "reward", "cost");
        var amount = string.IsNullOrWhiteSpace(rewardCost)
            ? rewardTitle
            : $"{rewardTitle} · {rewardCost} points";

        return new ChatEvent
        {
            Id = GetString(data, "redemptionId") ?? GetString(data, "eventId") ??
                 GetString(data, "id") ?? Guid.NewGuid().ToString("N"),
            Type = "redemption",
            Platform = ChatPlatform.Twitch,
            User = GetNestedString(data, "user", "name") ??
                   GetNestedString(data, "user", "login") ??
                   GetString(data, "userName") ??
                   "Twitch viewer",
            Amount = amount,
            Message = GetString(data, "userInput") ?? GetString(data, "message"),
            Timestamp = ParseDateTime(GetString(data, "createdAt") ??
                                      GetString(data, "redeemedAt") ??
                                      GetString(data, "publishedAt"))
        };
    }

    private static Task SendAsync(ClientWebSocket socket, object request, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private Task SendRequestAsync(object request, CancellationToken cancellationToken) =>
        _activeSocket is { State: WebSocketState.Open } socket
            ? SendAsync(socket, request, cancellationToken)
            : Task.FromException(new InvalidOperationException("Streamer.bot is not connected."));

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : element.TryGetProperty(property, out value) && value.ValueKind == JsonValueKind.Number
                ? value.GetRawText()
                : null;

    private static string? GetNestedString(JsonElement element, string parent, string property) =>
        element.TryGetProperty(parent, out var nested) ? GetString(nested, property) : null;

    private static bool GetNestedBoolean(JsonElement element, string parent, string property) =>
        element.TryGetProperty(parent, out var nested) &&
        nested.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.True;

    private static DateTimeOffset ParseDateTime(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.UtcNow;

    private void SetState(ConnectionState state)
    {
        _state = state;
        StateChanged?.Invoke(state);
    }

    private void SetHealth(ConnectionHealth health)
    {
        _health = health;
        HealthChanged?.Invoke(health);
    }
}