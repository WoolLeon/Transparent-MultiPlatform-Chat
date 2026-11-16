using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using MultiChatOverlay.Core.Models;

namespace MultiChatOverlay.Core.Sources;

public sealed class TwitchEventSubSource(string clientId, string broadcasterLogin, string accessToken) : IChatSource
{
    private static readonly Uri EventSubUri = new("wss://eventsub.wss.twitch.tv/ws");
    private static readonly Uri UsersUri = new("https://api.twitch.tv/helix/users");
    private static readonly Uri SubscriptionsUri = new("https://api.twitch.tv/helix/eventsub/subscriptions");
    private static readonly HttpClient HttpClient = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _clientId = Require(clientId, nameof(clientId));
    private readonly string _broadcasterLogin = Require(broadcasterLogin, nameof(broadcasterLogin));
    private readonly string _accessToken = Require(accessToken, nameof(accessToken));
    private CancellationTokenSource? _lifetime;
    private Task? _runTask;
    private ConnectionState _state = ConnectionState.Disconnected;

    public ChatPlatform Platform => ChatPlatform.Twitch;
    public ConnectionState State => _state;
    public event Action<ChatItem>? ItemReceived;
    public event Action<ConnectionState>? StateChanged;
    public event Action<Exception>? ErrorOccurred;

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

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var retry = TimeSpan.FromSeconds(1);
        var firstAttempt = true;
        Uri? reconnectUri = null;
        var subscriptionsMigrated = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            try
            {
                SetState(firstAttempt ? ConnectionState.Connecting : ConnectionState.Reconnecting);
                firstAttempt = false;
                await socket.ConnectAsync(reconnectUri ?? EventSubUri, cancellationToken);
                var result = await ReceiveSessionAsync(socket, subscriptionsMigrated, cancellationToken);
                if (result.ReconnectUri is not null)
                {
                    reconnectUri = result.ReconnectUri;
                    subscriptionsMigrated = true;
                    retry = TimeSpan.FromSeconds(1);
                    continue;
                }

                reconnectUri = null;
                subscriptionsMigrated = false;
                SetState(ConnectionState.Reconnecting);
            }
            catch (TwitchEventSubRequestException ex)
            {
                ErrorOccurred?.Invoke(ex);
                SetState(ConnectionState.Faulted);
                break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ErrorOccurred?.Invoke(ex);
                SetState(ConnectionState.Reconnecting);
                reconnectUri = null;
                subscriptionsMigrated = false;
            }

            if (cancellationToken.IsCancellationRequested)
                break;

            await Task.Delay(retry, cancellationToken);
            retry = TimeSpan.FromSeconds(Math.Min(retry.TotalSeconds * 2, 30));
        }
    }

    private async Task<SessionResult> ReceiveSessionAsync(
        ClientWebSocket socket,
        bool subscriptionsMigrated,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var welcomed = false;
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var content = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    return new SessionResult(null);
                content.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            using var document = JsonDocument.Parse(content.ToArray());
            var root = document.RootElement;
            if (!root.TryGetProperty("metadata", out var metadata) ||
                !metadata.TryGetProperty("message_type", out var messageTypeElement))
                continue;

            var messageType = messageTypeElement.GetString();
            if (messageType == "session_welcome")
            {
                if (welcomed)
                    continue;
                welcomed = true;
                if (!subscriptionsMigrated)
                {
                    var sessionId = root.GetProperty("payload").GetProperty("session").GetProperty("id").GetString()
                                    ?? throw new InvalidDataException("Twitch EventSub welcome message did not include a session ID.");
                    var broadcasterId = await GetBroadcasterIdAsync(cancellationToken);
                    await CreateSubscriptionAsync(
                        "channel.channel_points_custom_reward_redemption.add", sessionId, broadcasterId, cancellationToken);
                    await CreateSubscriptionAsync(
                        "channel.channel_points_automatic_reward_redemption.add", sessionId, broadcasterId, cancellationToken);
                }

                SetState(ConnectionState.Connected);
            }
            else if (messageType == "notification" && root.TryGetProperty("payload", out var payload) &&
                     payload.TryGetProperty("event", out var eventData))
            {
                var type = metadata.TryGetProperty("subscription_type", out var subscriptionType)
                    ? subscriptionType.GetString()
                    : null;
                var item = ParseRedemption(type, eventData);
                if (item is not null)
                    ItemReceived?.Invoke(item);
            }
            else if (messageType == "session_reconnect" &&
                     root.GetProperty("payload").GetProperty("session").TryGetProperty("reconnect_url", out var reconnectElement) &&
                     Uri.TryCreate(reconnectElement.GetString(), UriKind.Absolute, out var reconnectTarget))
            {
                return new SessionResult(reconnectTarget);
            }
        }

        return new SessionResult(null);
    }

    private async Task<string> GetBroadcasterIdAsync(CancellationToken cancellationToken)
    {
        var uri = new Uri($"{UsersUri}?login={Uri.EscapeDataString(_broadcasterLogin)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("Client-Id", _clientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response.StatusCode, body);
        using var document = JsonDocument.Parse(body);
        var users = document.RootElement.GetProperty("data");
        if (users.GetArrayLength() == 0)
            throw new InvalidDataException($"Twitch user '{_broadcasterLogin}' was not found.");
        return users[0].GetProperty("id").GetString()
               ?? throw new InvalidDataException("Twitch did not return a broadcaster user ID.");
    }

    private async Task CreateSubscriptionAsync(
        string type,
        string sessionId,
        string broadcasterId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SubscriptionsUri)
        {
            Content = JsonContent.Create(new
            {
                type,
                version = "1",
                condition = new { broadcaster_user_id = broadcasterId },
                transport = new { method = "websocket", session_id = sessionId }
            }, options: JsonOptions)
        };
        request.Headers.Add("Client-Id", _clientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response.StatusCode, body);
    }

    private static void EnsureSuccess(HttpStatusCode statusCode, string responseBody)
    {
        if ((int)statusCode is >= 200 and < 300)
            return;

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new TwitchEventSubRequestException(
                "Twitch EventSub could not authorize channel point events. Sign in again in Settings and grant channel:read:redemptions. The signed-in Twitch account must own the configured channel.");
        }

        var detail = string.IsNullOrWhiteSpace(responseBody) ? statusCode.ToString() : responseBody;
        throw new TwitchEventSubRequestException($"Twitch EventSub request failed ({(int)statusCode}): {detail}");
    }

    internal static ChatItem? ParseRedemption(string? subscriptionType, JsonElement eventData)
    {
        if (subscriptionType is not ("channel.channel_points_custom_reward_redemption.add" or
            "channel.channel_points_automatic_reward_redemption.add"))
            return null;

        var reward = eventData.TryGetProperty("reward", out var rewardElement) ? rewardElement : default;
        var title = GetString(reward, "title");
        if (string.IsNullOrWhiteSpace(title))
            title = GetString(reward, "type") ?? "Automatic reward";
        var cost = GetString(reward, "cost");
        var userName = GetString(eventData, "user_name") ?? GetString(eventData, "user_login") ?? "Twitch viewer";
        var message = GetString(eventData, "user_input");
        var timestamp = DateTimeOffset.TryParse(GetString(eventData, "redeemed_at"), out var parsedTimestamp)
            ? parsedTimestamp
            : DateTimeOffset.UtcNow;
        var id = GetString(eventData, "id") ?? $"{userName}:{title}:{timestamp:O}";

        return ChatItem.FromEvent(new ChatEvent
        {
            Id = id,
            Type = "redemption",
            Platform = ChatPlatform.Twitch,
            User = userName,
            Amount = string.IsNullOrWhiteSpace(cost) ? title : $"{title} · {cost} points",
            Message = string.IsNullOrWhiteSpace(message) ? null : message,
            Timestamp = timestamp
        });
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? value.ToString()
            : null;

    private static string Require(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameterName)
            : value.Trim();

    private void SetState(ConnectionState state)
    {
        if (_state == state)
            return;

        _state = state;
        StateChanged?.Invoke(state);
    }

    private sealed record SessionResult(Uri? ReconnectUri);

    private sealed class TwitchEventSubRequestException(string message) : Exception(message);
}
