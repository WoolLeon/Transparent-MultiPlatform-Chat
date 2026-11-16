using System.Net.WebSockets;
using System.Text;
using MultiChatOverlay.Core.Models;
using MultiChatOverlay.Core.Parsing;

namespace MultiChatOverlay.Core.Sources;

public sealed class TwitchIrcSource(string channel, string username, string accessToken) : IChatSource
{
    private readonly string _channel = NormalizeChannel(channel);
    private readonly string _username = NormalizeUsername(username);
    private readonly string _accessToken = ValidateToken(accessToken);
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
        while (!cancellationToken.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            try
            {
                SetState(firstAttempt ? ConnectionState.Connecting : ConnectionState.Reconnecting);
                firstAttempt = false;
                await socket.ConnectAsync(new Uri("wss://irc-ws.chat.twitch.tv:443"), cancellationToken);
                await SendAsync(socket, $"PASS oauth:{_accessToken}\r\n", cancellationToken);
                await SendAsync(socket, $"NICK {_username}\r\n", cancellationToken);
                await SendAsync(socket, "CAP REQ :twitch.tv/tags twitch.tv/commands\r\n", cancellationToken);
                await SendAsync(socket, $"JOIN #{_channel}\r\n", cancellationToken);
                await ReceiveLoopAsync(socket, cancellationToken);
                if (_state == ConnectionState.Connected)
                    retry = TimeSpan.FromSeconds(1);
                if (!cancellationToken.IsCancellationRequested)
                    SetState(ConnectionState.Reconnecting);
            }
            catch (TwitchAuthenticationException ex)
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
            }

            if (cancellationToken.IsCancellationRequested)
                break;

            await Task.Delay(retry, cancellationToken);
            retry = TimeSpan.FromSeconds(Math.Min(retry.TotalSeconds * 2, 30));
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var pending = new StringBuilder();
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return;

            pending.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            while (true)
            {
                var content = pending.ToString();
                var lineEnd = content.IndexOf('\n');
                if (lineEnd < 0)
                    break;

                var line = content[..lineEnd].TrimEnd('\r');
                pending.Clear().Append(content[(lineEnd + 1)..]);
                if (line.StartsWith("PING ", StringComparison.Ordinal))
                {
                    await SendAsync(socket, $"PONG {line[5..]}\r\n", cancellationToken);
                    continue;
                }

                if (line.StartsWith(":tmi.twitch.tv 001 ", StringComparison.Ordinal))
                    SetState(ConnectionState.Connected);
                if (TwitchIrcParser.IsAuthenticationFailureNotice(line))
                    throw new TwitchAuthenticationException(
                        "Twitch rejected the saved access token. Sign in to Twitch again in Settings.");

                foreach (var item in TwitchIrcParser.ParseItems(line))
                    ItemReceived?.Invoke(item);
            }
        }
    }

    private static Task SendAsync(ClientWebSocket socket, string text, CancellationToken cancellationToken) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, cancellationToken);

    private void SetState(ConnectionState state)
    {
        _state = state;
        StateChanged?.Invoke(state);
    }

    private static string NormalizeChannel(string channel)
    {
        var normalized = channel.Trim().TrimStart('#').ToLowerInvariant();
        if (normalized.Length is < 1 or > 25 ||
            normalized.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new ArgumentException("Enter a valid Twitch channel login.", nameof(channel));

        return normalized;
    }

    private static string NormalizeUsername(string username)
    {
        var normalized = username.Trim().ToLowerInvariant();
        if (normalized.Length is < 1 or > 25 ||
            normalized.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new ArgumentException("Enter the Twitch login associated with the access token.", nameof(username));

        return normalized;
    }

    private static string ValidateToken(string token)
    {
        var normalized = token.Trim();
        if (normalized.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[6..];
        if (normalized.Length == 0 || normalized.Contains('\r') || normalized.Contains('\n'))
            throw new ArgumentException("A Twitch User Access Token is required.", nameof(token));

        return normalized;
    }

    private sealed class TwitchAuthenticationException(string message) : Exception(message);
}
