using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using MultiChatOverlay.Core.Models;

namespace MultiChatOverlay.Core.Sources;

public sealed class FacebookLiveCommentsSource(string liveVideoId, string accessToken) : IChatSource
{
    private static readonly HttpClient HttpClient = new();
    private const string GraphVersion = "v27.0";
    private const int SeenIdCapacity = 1000;
    private readonly string _liveVideoId = ValidateRequired(liveVideoId, nameof(liveVideoId));
    private readonly string _accessToken = ValidateRequired(accessToken, nameof(accessToken));
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new();
    private CancellationTokenSource? _lifetime;
    private Task? _runTask;
    private ConnectionState _state = ConnectionState.Disconnected;
    private bool _initialRead = true;

    public ChatPlatform Platform => ChatPlatform.Facebook;
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
            try
            {
                SetState(firstAttempt ? ConnectionState.Connecting : ConnectionState.Reconnecting);
                firstAttempt = false;
                using var request = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri());
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                using var response = await HttpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                SetState(ConnectionState.Connected);
                retry = TimeSpan.FromSeconds(1);
                ParseComments(document.RootElement);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                ErrorOccurred?.Invoke(ex);
                SetState(ConnectionState.Reconnecting);
                await Task.Delay(retry, cancellationToken);
                retry = TimeSpan.FromSeconds(Math.Min(retry.TotalSeconds * 2, 30));
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
                SetState(ConnectionState.Reconnecting);
                await Task.Delay(retry, cancellationToken);
                retry = TimeSpan.FromSeconds(Math.Min(retry.TotalSeconds * 2, 30));
            }
        }
    }

    private Uri BuildRequestUri() => new(
        $"https://graph.facebook.com/{GraphVersion}/{Uri.EscapeDataString(_liveVideoId)}/comments" +
        "?filter=stream&order=reverse_chronological&fields=id%2Cfrom%2Cmessage%2Ccreated_time&limit=100");

    private void ParseComments(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var comments) || comments.ValueKind != JsonValueKind.Array)
            return;

        var parsed = new List<ChatMessage>();
        foreach (var comment in comments.EnumerateArray())
        {
            var id = GetString(comment, "id");
            var text = GetString(comment, "message");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(text) || !_seen.Add(id))
                continue;
            _seenOrder.Enqueue(id);
            while (_seenOrder.Count > SeenIdCapacity)
                _seen.Remove(_seenOrder.Dequeue());

            var from = comment.TryGetProperty("from", out var fromElement) ? fromElement : default;
            parsed.Add(new ChatMessage
            {
                Id = id,
                Platform = ChatPlatform.Facebook,
                Username = GetString(from, "id") ?? GetString(from, "name") ?? "Facebook viewer",
                DisplayName = GetString(from, "name") ?? "Facebook viewer",
                MessageParts = [new TextPart(text)],
                Timestamp = ParseTimestamp(GetString(comment, "created_time"))
            });
        }

        if (_initialRead)
        {
            _initialRead = false;
            return;
        }

        foreach (var message in parsed.AsEnumerable().Reverse())
            ItemReceived?.Invoke(ChatItem.FromMessage(message));
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp)
            ? timestamp
            : DateTimeOffset.UtcNow;

    private static string ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A value is required.", parameterName);
        return value.Trim();
    }

    private void SetState(ConnectionState state)
    {
        _state = state;
        StateChanged?.Invoke(state);
    }
}
