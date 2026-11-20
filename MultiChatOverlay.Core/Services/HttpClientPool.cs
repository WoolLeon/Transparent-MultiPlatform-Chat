using System.Net.Http;

namespace MultiChatOverlay.Core.Services;

/// <summary>
/// Centralized HttpClient pool for connection reuse across all chat sources.
/// Reduces socket exhaustion, DNS lookups, and TLS handshakes.
/// </summary>
public static class HttpClientPool
{
    private static readonly HttpClient _sharedClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private static readonly HttpClient _emoteClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private static readonly HttpClient _facebookClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    private static readonly HttpClient _youtubeClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        BaseAddress = new Uri("https://www.youtube.com/")
    };

    static HttpClientPool()
    {
        // Enable connection pooling and keep-alive
        _sharedClient.DefaultRequestHeaders.ConnectionClose = false;
        _emoteClient.DefaultRequestHeaders.ConnectionClose = false;
        _facebookClient.DefaultRequestHeaders.ConnectionClose = false;
        _youtubeClient.DefaultRequestHeaders.ConnectionClose = false;
    }

    /// <summary>
    /// General-purpose client for API calls (Twitch Helix, EventSub, etc.)
    /// </summary>
    public static HttpClient Shared => _sharedClient;

    /// <summary>
    /// Client optimized for emote provider APIs (7TV, BTTV, FFZ)
    /// Shorter timeout, separate connection pool
    /// </summary>
    public static HttpClient Emotes => _emoteClient;

    /// <summary>
    /// Client for Facebook Graph API polling
    /// Configurable timeout via FacebookLiveCommentsSource
    /// </summary>
    public static HttpClient Facebook => _facebookClient;

    /// <summary>
    /// Client for YouTube InnerTube API - requires BaseAddress set to YouTube domain
    /// </summary>
    public static HttpClient YouTube => _youtubeClient;

    /// <summary>
    /// Warm up connections on startup to avoid first-request latency
    /// </summary>
    public static async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        var tasks = new[]
        {
            WarmupClientAsync(_sharedClient, "https://api.twitch.tv/helix", cancellationToken),
            WarmupClientAsync(_emoteClient, "https://7tv.io/v3", cancellationToken),
            WarmupClientAsync(_emoteClient, "https://api.betterttv.net/3", cancellationToken),
            WarmupClientAsync(_emoteClient, "https://api.frankerfacez.com/v1", cancellationToken),
            WarmupClientAsync(_facebookClient, "https://graph.facebook.com", cancellationToken),
            WarmupClientAsync(_youtubeClient, "https://www.youtube.com", cancellationToken)
        };

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Dispose all HttpClient instances. Call on application shutdown to release connections.
    /// </summary>
    public static void Dispose()
    {
        _sharedClient.Dispose();
        _emoteClient.Dispose();
        _facebookClient.Dispose();
        _youtubeClient.Dispose();
    }

    private static async Task WarmupClientAsync(HttpClient client, string baseUrl, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, baseUrl);
            await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch
        {
            // Ignore warmup failures
        }
    }
}