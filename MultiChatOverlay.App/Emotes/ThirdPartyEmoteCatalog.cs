using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MultiChatOverlay.App.Logging;
using MultiChatOverlay.Core.Models;
using MultiChatOverlay.Core.Services;
using System.Diagnostics;

namespace MultiChatOverlay.App.Emotes;

public sealed class ThirdPartyEmoteCatalog
{
    private readonly EmoteMessageMatcher _matcher = new();
    private Task? _channelEmoteTask;

    public void Clear()
    {
        _matcher.Clear();
        _channelEmoteTask = null;
    }

    /// <summary>
    /// Load global emotes immediately; channel emotes load lazily when channelId becomes available.
    /// </summary>
    public async Task LoadAsync(
        string channel,
        string? clientId,
        string? accessToken,
        bool useSevenTv,
        bool useBetterTtv,
        bool useFrankerFaceZ,
        CancellationToken cancellationToken = default)
    {
        _matcher.Clear();

        string? channelId = null;
        var needsChannelId = (useSevenTv || useBetterTtv) &&
                             !string.IsNullOrWhiteSpace(channel) &&
                             !string.IsNullOrWhiteSpace(clientId) &&
                             !string.IsNullOrWhiteSpace(accessToken);

        // Phase 1: Load global emotes immediately (always needed)
        var globalTasks = new List<Task>();
        if (useSevenTv)
            globalTasks.Add(LoadProviderAsync("7TV", () => LoadSevenTvGlobalAsync(_matcher, cancellationToken), cancellationToken));
        if (useBetterTtv)
            globalTasks.Add(LoadProviderAsync("BetterTTV", () => LoadBetterTtvGlobalAsync(_matcher, cancellationToken), cancellationToken));
        if (useFrankerFaceZ)
            globalTasks.Add(LoadProviderAsync("FrankerFaceZ", () => LoadFrankerFaceZGlobalAsync(_matcher, cancellationToken), cancellationToken));

        await Task.WhenAll(globalTasks);

        // Phase 2: Resolve channel ID and load channel emotes if possible
        if (needsChannelId)
        {
            try
            {
                channelId = await ResolveTwitchChannelIdAsync(channel, clientId!, accessToken!, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException ||
                                           ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                Log.Logger.Warning(ex, "Unable to resolve Twitch channel for third-party emotes");
            }
        }
        else if ((useSevenTv || useBetterTtv) && !string.IsNullOrWhiteSpace(channel))
        {
            Log.Logger.Warning(
                "Channel-specific 7TV/BetterTTV emotes require a signed-in Twitch account and configured Twitch Client ID");
        }

        // Start channel emote loading in background (non-blocking)
        if ((useSevenTv || useBetterTtv) && channelId is not null)
        {
            _channelEmoteTask = Task.Run(async () =>
            {
                try
                {
                    if (useSevenTv)
                        await LoadProviderAsync("7TV-Channel", () => LoadSevenTvChannelAsync(_matcher, channelId!, cancellationToken), cancellationToken);
                    if (useBetterTtv)
                        await LoadProviderAsync("BetterTTV-Channel", () => LoadBetterTtvChannelAsync(_matcher, channelId!, cancellationToken), cancellationToken);
                    Log.Logger.Information("Loaded channel-specific third-party emotes");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Logger.Warning(ex, "Failed to load channel-specific emotes");
                }
            }, cancellationToken);
        }
        else if ((useSevenTv || useBetterTtv) && !string.IsNullOrWhiteSpace(channel) && channelId is null)
        {
            Log.Logger.Warning("No Twitch channel ID resolved for {TwitchChannel}; only global 7TV/BetterTTV emotes are available", channel);
        }

        Log.Logger.Information(
            "Loaded third-party emotes: 7TV={SevenTv}, BetterTTV={BetterTtv}, FrankerFaceZ={FrankerFaceZ}",
            _matcher.CountForProvider("7tv"),
            _matcher.CountForProvider("betterttv"),
            _matcher.CountForProvider("ffz"));
    }

    /// <summary>
    /// Call when Twitch IRC connects with a resolved channel ID to trigger channel emote loading.
    /// </summary>
    public void LoadChannelEmotes(string channelId, bool useSevenTv, bool useBetterTtv, CancellationToken cancellationToken = default)
    {
        if (_channelEmoteTask is not null && !_channelEmoteTask.IsCompleted)
            return; // Already loading

        if ((useSevenTv || useBetterTtv) && !string.IsNullOrWhiteSpace(channelId))
        {
            _channelEmoteTask = Task.Run(async () =>
            {
                try
                {
                    if (useSevenTv)
                        await LoadProviderAsync("7TV-Channel", () => LoadSevenTvChannelAsync(_matcher, channelId, cancellationToken), cancellationToken);
                    if (useBetterTtv)
                        await LoadProviderAsync("BetterTTV-Channel", () => LoadBetterTtvChannelAsync(_matcher, channelId, cancellationToken), cancellationToken);
                    Log.Logger.Information("Loaded channel-specific third-party emotes (on-connect)");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Logger.Warning(ex, "Failed to load channel-specific emotes (on-connect)");
                }
            }, cancellationToken);
        }
    }

    public ChatMessage Apply(ChatMessage message) => _matcher.Apply(message);

    private static async Task<string?> ResolveTwitchChannelIdAsync(
        string channel,
        string clientId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.twitch.tv/helix/users?login={Uri.EscapeDataString(channel.Trim().TrimStart('#'))}");
        request.Headers.Add("Client-Id", clientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await HttpClientPool.Shared.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array ||
            data.GetArrayLength() == 0 ||
            !data[0].TryGetProperty("id", out var id))
            return null;
        return id.GetString();
    }

    private static async Task LoadSevenTvGlobalAsync(EmoteMessageMatcher matcher, CancellationToken cancellationToken)
    {
        using var global = await GetJsonAsync("https://7tv.io/v3/emote-sets/global", cancellationToken);
        AddSevenTvEmotes(matcher, global.RootElement);
    }

    private static async Task LoadSevenTvChannelAsync(EmoteMessageMatcher matcher, string channelId, CancellationToken cancellationToken)
    {
        using var channel = await GetJsonAsync(
            $"https://7tv.io/v3/users/twitch/{Uri.EscapeDataString(channelId)}", cancellationToken);
        if (channel.RootElement.TryGetProperty("emote_set", out var set))
            AddSevenTvEmotes(matcher, set, replaceExisting: true);
    }

    private static async Task LoadBetterTtvGlobalAsync(EmoteMessageMatcher matcher, CancellationToken cancellationToken)
    {
        using var global = await GetJsonAsync("https://api.betterttv.net/3/cached/emotes/global", cancellationToken);
        AddBetterTtvEmotes(matcher, global.RootElement);
    }

    private static async Task LoadBetterTtvChannelAsync(EmoteMessageMatcher matcher, string channelId, CancellationToken cancellationToken)
    {
        using var channel = await GetJsonAsync(
            $"https://api.betterttv.net/3/cached/users/twitch/{Uri.EscapeDataString(channelId)}", cancellationToken);
        foreach (var property in new[] { "channelEmotes", "sharedEmotes" })
        {
            if (channel.RootElement.TryGetProperty(property, out var emotes))
                AddBetterTtvEmotes(matcher, emotes);
        }
    }

    private static async Task LoadFrankerFaceZGlobalAsync(EmoteMessageMatcher matcher, CancellationToken cancellationToken)
    {
        using var global = await GetJsonAsync("https://api.frankerfacez.com/v1/set/global", cancellationToken);
        AddFrankerFaceZSets(matcher, global.RootElement);
    }

    private static async Task LoadProviderAsync(string provider, Func<Task> load, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await load();
            MultiChatEventSource.Log.EmoteProviderLoaded(provider, 0); // Count will be logged separately
            MultiChatEventSource.Log.EmoteLoadDuration(provider, sw.Elapsed.TotalMilliseconds, fromCache: false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException ||
                                       ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            MultiChatEventSource.Log.EmoteProviderFailed(provider, ex.Message);
            Log.Logger.Warning(ex, "Unable to load {EmoteProvider} emotes", provider);
        }
    }

    private static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        var cachePath = GetCachePath(url);
        Exception? requestFailure = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("MultiChatOverlay/1.0");
                using var response = await HttpClientPool.Emotes.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                await CacheResponseAsync(cachePath, content, cancellationToken);
                return JsonDocument.Parse(content);
            }
            catch (Exception ex) when (
                !cancellationToken.IsCancellationRequested &&
                (ex is HttpRequestException || ex is TaskCanceledException))
            {
                requestFailure = ex;
                if (attempt == 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
        }

        if (File.Exists(cachePath))
        {
            try
            {
                var cachedContent = await File.ReadAllBytesAsync(cachePath, cancellationToken);
                MultiChatEventSource.Log.EmoteCacheHit(new Uri(url).Host, cachePath);
                Log.Logger.Warning(requestFailure,
                    "Using cached emote data for {EmoteDataUrl} after the refresh request failed", url);
                return JsonDocument.Parse(cachedContent);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Log.Logger.Warning(ex, "Unable to read cached emote data for {EmoteDataUrl}", url);
            }
        }

        throw requestFailure ?? new HttpRequestException($"Unable to load emote data from {url}.");
    }

    private static string GetCachePath(string url)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MultiChatOverlay",
            "emote-cache",
            $"{key}.json");
    }

    private static async Task CacheResponseAsync(
        string cachePath,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var temporaryPath = cachePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken);
            File.Move(temporaryPath, cachePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Logger.Warning(ex, "Unable to cache emote data at {EmoteCachePath}", cachePath);
        }
    }

    private static void AddSevenTvEmotes(EmoteMessageMatcher matcher, JsonElement set, bool replaceExisting = false)
    {
        if (!set.TryGetProperty("emotes", out var emotes) || emotes.ValueKind != JsonValueKind.Array)
            return;

        foreach (var emote in emotes.EnumerateArray())
        {
            if (!emote.TryGetProperty("name", out var nameElement) || nameElement.GetString() is not { Length: > 0 } name ||
                !emote.TryGetProperty("id", out var idElement) || idElement.GetString() is not { Length: > 0 } id)
                continue;

            // Check if emote is animated - 7TV uses WebP which supports animation natively
            var isAnimated = emote.TryGetProperty("data", out var data) &&
                             data.TryGetProperty("animated", out var animated) &&
                             animated.GetBoolean();

            var url = $"https://cdn.7tv.app/emote/{Uri.EscapeDataString(id)}/2x.webp";
            if (emote.TryGetProperty("data", out data) &&
                data.TryGetProperty("host", out var host) &&
                host.TryGetProperty("url", out var hostUrl) &&
                hostUrl.GetString() is { Length: > 0 } baseUrl &&
                host.TryGetProperty("files", out var files) &&
                files.ValueKind == JsonValueKind.Array)
            {
                // Prefer animated (4x/3x) then static (2x/1x) based on emote type
                var preferredPatterns = isAnimated
                    ? new[] { "4x", "3x", "2x", "1x" }
                    : new[] { "2x", "1x" };

                var file = files.EnumerateArray().FirstOrDefault(candidate =>
                    candidate.TryGetProperty("name", out var fileName) &&
                    fileName.GetString() is { } value &&
                    preferredPatterns.Any(p => value.Contains(p, StringComparison.OrdinalIgnoreCase)));
                if (file.ValueKind == JsonValueKind.Object &&
                    file.TryGetProperty("name", out var selectedFile) &&
                    selectedFile.GetString() is { Length: > 0 } fileName)
                    url = $"{NormalizeUrl(baseUrl)}/{Uri.EscapeDataString(fileName)}";
            }

            Add(matcher, name, url, "7tv", replaceExisting);
        }
    }

    private static void AddBetterTtvEmotes(EmoteMessageMatcher matcher, JsonElement emotes)
    {
        if (emotes.ValueKind != JsonValueKind.Array)
            return;

        foreach (var emote in emotes.EnumerateArray())
        {
            if (emote.TryGetProperty("code", out var codeElement) && codeElement.GetString() is { Length: > 0 } code &&
                emote.TryGetProperty("id", out var idElement) && idElement.GetString() is { Length: > 0 } id)
            {
                // Check for animated emote - BTTV uses /3x for animated GIF/WebP
                var isAnimated = emote.TryGetProperty("animated", out var animated) && animated.GetBoolean();
                var url = isAnimated
                    ? $"https://cdn.betterttv.net/emote/{Uri.EscapeDataString(id)}/3x"
                    : $"https://cdn.betterttv.net/emote/{Uri.EscapeDataString(id)}/2x";
                Add(matcher, code, url, "betterttv");
            }
        }
    }

    private static void AddFrankerFaceZSets(EmoteMessageMatcher matcher, JsonElement root)
    {
        if (!root.TryGetProperty("sets", out var sets) || sets.ValueKind != JsonValueKind.Object)
            return;

        foreach (var set in sets.EnumerateObject())
        {
            if (!set.Value.TryGetProperty("emoticons", out var emotes) || emotes.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var emote in emotes.EnumerateArray())
            {
                if (!emote.TryGetProperty("name", out var nameElement) ||
                    nameElement.GetString() is not { Length: > 0 } name ||
                    !emote.TryGetProperty("urls", out var urls) ||
                    urls.ValueKind != JsonValueKind.Object)
                    continue;

                // Check for animated emote - FFZ uses "4" for animated (or "2" for static)
                var isAnimated = emote.TryGetProperty("animated", out var animated) && animated.GetBoolean();
                var urlKey = isAnimated ? "4" : "2";
                var image = urls.TryGetProperty(urlKey, out var selected)
                    ? selected.GetString()
                    : urls.TryGetProperty("2", out var large) ? large.GetString()
                    : urls.TryGetProperty("1", out var small) ? small.GetString() : null;
                if (!string.IsNullOrWhiteSpace(image))
                    Add(matcher, name, NormalizeUrl(image), "ffz");
            }
        }
    }

    private static void Add(EmoteMessageMatcher matcher, string name, string url, string provider, bool replaceExisting = false) =>
        matcher.Add(new EmotePart(name, url, provider), replaceExisting);

    private static string NormalizeUrl(string url) =>
        url.StartsWith("//", StringComparison.Ordinal) ? $"https:{url}" : url;
}