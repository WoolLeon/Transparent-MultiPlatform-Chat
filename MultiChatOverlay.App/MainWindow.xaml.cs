using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using MultiChatOverlay.App.Logging;
using MultiChatOverlay.App.Settings;
using MultiChatOverlay.App.Emotes;
using MultiChatOverlay.Core.Models;
using MultiChatOverlay.Core.Services;
using System.Diagnostics;
using MultiChatOverlay.Core.Sources;

namespace MultiChatOverlay.App;

public partial class MainWindow : Window
{
    private const int WmHotkey = 0x0312;
    private const int WmShowOverlay = 0x8000 + 0x351;
    private const int HotkeyLock = 1;
    private const int HotkeyVisibility = 2;
    private const int HotkeyClear = 3;
    private const int HotkeyOpacityUp = 4;
    private const int HotkeyOpacityDown = 5;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const int VkL = 0x4C;
    private const int VkO = 0x4F;
    private const int VkC = 0x43;
    private const int VkUp = 0x26;
    private const int VkDown = 0x28;
    private const int GwlExstyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExLayered = 0x00080000L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;

    private static readonly JsonSerializerOptions ChatJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // Batching queue for WebView2 messages
    private readonly ConcurrentQueue<object> _pendingWebViewMessages = new();
    private DispatcherTimer? _flushTimer;

    // Auto-hide on inactivity
    private DispatcherTimer? _autoHideTimer;
    private DateTime _lastMessageTime = DateTime.UtcNow;
    private bool _isAutoHidden = false;
    private double _normalOpacity = 0.96;

    private readonly SettingsStore _settingsStore = new();
    private readonly MessageDeduplicator _deduplicator = new();
    private readonly ChatEventDeduplicator _eventDeduplicator = new();
    private readonly ThirdPartyEmoteCatalog _thirdPartyEmotes = new();
    private readonly ChatLogExporter _chatLogExporter = new();
    private readonly List<IChatSource> _sources = [];
    private OverlaySettings _settings = new();
    private HwndSource? _hwndSource;
    private Forms.NotifyIcon? _trayIcon;
    private FileSystemWatcher? _cssWatcher;
    private CancellationTokenSource? _cssReloadDelay;
    private bool _shuttingDown;
    private bool _shutdownReady;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings = await _settingsStore.LoadAsync();
            RestoreWindowBounds();
            Opacity = Math.Clamp(_settings.Opacity, 0.25, 1);
            SetLocked(_settings.IsLocked, save: false);
            SetupHotkeys();
            SetupTrayIcon();
            await InitializeChatViewAsync();
            InitializeMessageBatching();
            await HttpClientPool.WarmupAsync();

            // Start chat log exporter if enabled
            if (_settings.EnableChatLogExport)
            {
                _chatLogExporter.Start(_settings.ChatLogExportPath);
                Log.Logger.Information("Chat log export enabled: JSONL={JsonlPath}, Count={Count}",
                    _chatLogExporter.JsonlPath, 0);
            }

            MultiChatEventSource.Log.ApplicationStarting(GetVersion());

            // Initialize auto-hide timer if enabled
            InitializeAutoHide();

            await StartSourcesAsync();
        }
        catch (Exception ex)
        {
            MultiChatEventSource.Log.UnhandledException(ex.GetType().Name, ex.Message, ex.StackTrace ?? "");
            Log.Logger.Error(ex, "Unable to initialize overlay");
            System.Windows.MessageBox.Show($"Unable to start the overlay: {ex.Message}", "Multi Chat Overlay",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string GetVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        return version;
    }

    private void InitializeMessageBatching()
    {
        _flushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) }; // ~60fps
        _flushTimer.Tick += async (_, _) => await FlushMessageBatchAsync();
        _flushTimer.Start();
    }

    private void InitializeAutoHide()
    {
        _normalOpacity = Opacity;

        if (_settings.EnableAutoHide)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(1, _settings.AutoHideSeconds));
            _autoHideTimer = new DispatcherTimer { Interval = interval };
            _autoHideTimer.Tick += AutoHideTimer_Tick;
            _autoHideTimer.Start();
            _lastMessageTime = DateTime.UtcNow;
            _autoHideGracePeriodEnd = DateTime.UtcNow.AddSeconds(10); // 10s grace period on startup
        }
    }

    private DateTime _autoHideGracePeriodEnd = DateTime.MinValue;

    private void AutoHideTimer_Tick(object? sender, EventArgs e)
    {
        if (!_settings.EnableAutoHide || _isAutoHidden)
            return;

        // Skip auto-hide during startup grace period
        if (DateTime.UtcNow < _autoHideGracePeriodEnd)
            return;

        var inactiveSeconds = (DateTime.UtcNow - _lastMessageTime).TotalSeconds;
        if (inactiveSeconds >= _settings.AutoHideSeconds)
        {
            FadeToAutoHideOpacity();
        }
    }

    private void FadeToAutoHideOpacity()
    {
        if (_isAutoHidden)
            return;

        _isAutoHidden = true;
        // Never go below 5% opacity to prevent complete disappearance
        var targetOpacity = Math.Clamp(_settings.AutoHideOpacity, 0.05, 0.5);
        AnimateOpacity(targetOpacity);
        MultiChatEventSource.Log.ApplicationShuttingDown("Auto-hidden due to inactivity");
    }

    private void RestoreFromAutoHide()
    {
        if (!_isAutoHidden)
            return;

        _isAutoHidden = false;
        AnimateOpacity(_normalOpacity);
    }

    private void AnimateOpacity(double targetOpacity)
    {
        var animation = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = targetOpacity,
            Duration = TimeSpan.FromMilliseconds(500),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        this.BeginAnimation(OpacityProperty, animation);
    }

    private void UpdateLastMessageTime()
    {
        _lastMessageTime = DateTime.UtcNow;
        if (_isAutoHidden)
        {
            RestoreFromAutoHide();
        }
    }

    private async Task FlushMessageBatchAsync()
    {
        if (_pendingWebViewMessages.IsEmpty)
            return;

        var batch = new List<object>();
        while (_pendingWebViewMessages.TryDequeue(out var item) && batch.Count < 100)
        {
            batch.Add(item);
        }

        if (batch.Count > 0)
        {
            var json = JsonSerializer.Serialize(batch, ChatJsonOptions);
            ChatView.CoreWebView2?.PostWebMessageAsJson(json);
        }
    }

    private void QueueWebViewMessage(object message)
    {
        _pendingWebViewMessages.Enqueue(message);
    }

    private async Task InitializeChatViewAsync()
    {
        await ChatView.EnsureCoreWebView2Async();
        ChatView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        ChatView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        ChatView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        ChatView.Source = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "chat.html"));
        ChatView.NavigationCompleted += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_settings.CustomCss))
                await ApplyCustomCssAsync(_settings.CustomCss);
            if (!string.IsNullOrWhiteSpace(_settings.CustomCssPath))
                await LoadCustomCssFileAsync(_settings.CustomCssPath);
            ConfigureCssWatcher();
        };
    }

    private async Task StartSourcesAsync()
    {
        var twitchToken = string.IsNullOrWhiteSpace(_settings.TwitchOAuthTokenProtected)
            ? null
            : TryUnprotectToken(_settings.TwitchOAuthTokenProtected, ChatPlatform.Twitch);
        InitializeConnectionStatus(twitchToken);
        var twitchClientId = string.IsNullOrWhiteSpace(_settings.TwitchClientId)
            ? null
            : _settings.TwitchClientId;

        // Load global emotes immediately; channel emotes will load lazily when Twitch connects
        // Load global emotes if ANY platform is enabled (global emotes work for all platforms)
        var anyPlatformEnabled = _settings.ShowTwitch || _settings.ShowYouTube || _settings.ShowKick ||
                                 _settings.ShowFacebook || _settings.ShowStreamerBot;
        if (anyPlatformEnabled)
        {
            await _thirdPartyEmotes.LoadAsync(
                _settings.TwitchChannel,
                twitchClientId,
                twitchToken,
                _settings.UseSevenTvEmotes,
                _settings.UseBetterTtvEmotes,
                _settings.UseFrankerFaceZEmotes);
        }
        else
        {
            _thirdPartyEmotes.Clear();
        }

        if (_settings.ShowTwitch && !string.IsNullOrWhiteSpace(_settings.TwitchChannel) &&
            !string.IsNullOrWhiteSpace(_settings.TwitchUsername) &&
            twitchToken is not null)
        {
            try
            {
                var refreshToken = string.IsNullOrWhiteSpace(_settings.TwitchRefreshTokenProtected)
                    ? null
                    : TryUnprotectToken(_settings.TwitchRefreshTokenProtected, ChatPlatform.Twitch);
                var (tokens, login) = await OAuthLoginService.EnsureValidTwitchTokenAsync(
                    _settings.TwitchClientId,
                    twitchToken,
                    refreshToken);
                twitchToken = tokens.AccessToken;
                _settings.TwitchUsername = login;
                _settings.TwitchOAuthTokenProtected = SettingsStore.ProtectToken(tokens.AccessToken);
                if (!string.IsNullOrWhiteSpace(tokens.RefreshToken))
                    _settings.TwitchRefreshTokenProtected = SettingsStore.ProtectToken(tokens.RefreshToken);
                await _settingsStore.SaveAsync(_settings);
            }
            catch (System.Net.Http.HttpRequestException ex) when (
                ex.StatusCode is null || (int)ex.StatusCode >= 500)
            {
                Log.Logger.Warning(ex, "Unable to validate the saved Twitch token; trying the existing token");
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Logger.Error(ex, "Unable to authenticate the saved Twitch account");
                UpdateConnectionStatus(ChatPlatform.Twitch, ConnectionState.Faulted);
                TwitchStatus.ToolTip = $"Twitch: {ex.Message}";
                twitchToken = null;
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                Log.Logger.Error(ex, "Twitch rejected the saved access token");
                UpdateConnectionStatus(ChatPlatform.Twitch, ConnectionState.Faulted);
                TwitchStatus.ToolTip = "Twitch rejected the saved access token. Sign in again in Settings.";
                twitchToken = null;
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
            {
                Log.Logger.Error(ex, "Unable to authenticate the saved Twitch account");
                UpdateConnectionStatus(ChatPlatform.Twitch, ConnectionState.Faulted);
                TwitchStatus.ToolTip = $"Twitch: {ex.Message}";
                twitchToken = null;
            }

            if (twitchToken is not null)
            {
                var twitch = new TwitchIrcSource(_settings.TwitchChannel, _settings.TwitchUsername, twitchToken);
                twitch.HealthChanged += health => Dispatcher.Invoke(() => OnSourceHealthChanged(ChatPlatform.Twitch, health));
                twitch.StateChanged += state => Dispatcher.Invoke(() =>
                {
                    if (state == ConnectionState.Connected)
                        MultiChatEventSource.Log.SourceConnected("Twitch", _settings.TwitchChannel);
                    else if (state == ConnectionState.Faulted)
                        MultiChatEventSource.Log.SourceFailed("Twitch", TwitchStatus.ToolTip?.ToString() ?? "");
                });
                RegisterSource(twitch);
                TwitchStatus.ToolTip = $"Twitch: #{_settings.TwitchChannel}";
                MultiChatEventSource.Log.SourceConnecting("Twitch", $"Channel: #{_settings.TwitchChannel}");
                await twitch.StartAsync();

                // Trigger channel emote loading now that we have a valid connection
                _thirdPartyEmotes.LoadChannelEmotes(
                    _settings.TwitchUsername, // Use username as channel ID fallback
                    _settings.UseSevenTvEmotes,
                    _settings.UseBetterTtvEmotes);

                if (!string.IsNullOrWhiteSpace(_settings.TwitchClientId))
                {
                    var eventSub = new TwitchEventSubSource(
                        _settings.TwitchClientId, _settings.TwitchUsername, twitchToken);
                    eventSub.HealthChanged += health => Dispatcher.Invoke(() => OnSourceHealthChanged(ChatPlatform.Twitch, health));
                    RegisterSource(eventSub, displayConnectionStatus: false);
                    await eventSub.StartAsync();
                }
                else
                {
                    Log.Logger.Warning("Twitch EventSub is disabled because the Twitch OAuth client ID is missing");
                    TwitchStatus.ToolTip =
                        $"Twitch: Chat connected to #{_settings.TwitchChannel}; configure the Twitch client ID to receive channel point events";
                }
            }
        }

        if (_settings.ShowYouTube && !string.IsNullOrWhiteSpace(_settings.YouTubeTarget))
        {
            var youtube = new YouTubeInnerTubeChatSource(_settings.YouTubeTarget);
            youtube.HealthChanged += health => Dispatcher.Invoke(() => OnSourceHealthChanged(ChatPlatform.YouTube, health));
            youtube.StateChanged += state => Dispatcher.Invoke(() =>
            {
                if (state == ConnectionState.Connected)
                    MultiChatEventSource.Log.SourceConnected("YouTube", _settings.YouTubeTarget);
                else if (state == ConnectionState.Faulted)
                    MultiChatEventSource.Log.SourceFailed("YouTube", YouTubeStatus.ToolTip?.ToString() ?? "");
            });
            RegisterSource(youtube);
            MultiChatEventSource.Log.SourceConnecting("YouTube", _settings.YouTubeTarget);
            await youtube.StartAsync();
        }

        if (_settings.ShowFacebook && !string.IsNullOrWhiteSpace(_settings.FacebookLiveVideoId) &&
            !string.IsNullOrWhiteSpace(_settings.FacebookAccessTokenProtected))
        {
            var token = TryUnprotectToken(_settings.FacebookAccessTokenProtected, ChatPlatform.Facebook);
            if (token is not null)
            {
                var facebook = new FacebookLiveCommentsSource(
                    _settings.FacebookLiveVideoId,
                    token,
                    _settings.FacebookPollIntervalSeconds,
                    _settings.FacebookMaxPollIntervalSeconds);
                facebook.HealthChanged += health => Dispatcher.Invoke(() => OnSourceHealthChanged(ChatPlatform.Facebook, health));
                facebook.StateChanged += state => Dispatcher.Invoke(() =>
                {
                    if (state == ConnectionState.Connected)
                        MultiChatEventSource.Log.SourceConnected("Facebook", _settings.FacebookLiveVideoId);
                    else if (state == ConnectionState.Faulted)
                        MultiChatEventSource.Log.SourceFailed("Facebook", FacebookStatus.ToolTip?.ToString() ?? "");
                });
                RegisterSource(facebook);
                MultiChatEventSource.Log.SourceConnecting("Facebook", _settings.FacebookLiveVideoId);
                await facebook.StartAsync();
            }
        }

        if (_settings.ShowStreamerBot && Uri.TryCreate(_settings.StreamerBotUrl, UriKind.Absolute, out var uri) &&
            uri.Scheme is "ws" or "wss")
        {
            var streamerBot = new StreamerBotSource(uri);
            streamerBot.HealthChanged += health => Dispatcher.Invoke(() => OnSourceHealthChanged(ChatPlatform.StreamerBot, health));
            streamerBot.StateChanged += state => Dispatcher.Invoke(() =>
            {
                if (state == ConnectionState.Connected)
                    MultiChatEventSource.Log.SourceConnected("Streamer.bot", _settings.StreamerBotUrl);
                else if (state == ConnectionState.Faulted)
                    MultiChatEventSource.Log.SourceFailed("Streamer.bot", StreamerBotStatus.ToolTip?.ToString() ?? "");
            });
            RegisterSource(streamerBot);
            MultiChatEventSource.Log.SourceConnecting("Streamer.bot", _settings.StreamerBotUrl);
            await streamerBot.StartAsync();
        }
    }

    private void OnSourceHealthChanged(ChatPlatform platform, ConnectionHealth health)
    {
        // Adaptive behavior: reduce Facebook polling if unhealthy
        if (platform == ChatPlatform.Facebook && health.ShouldReducePolling)
        {
            var fbSource = _sources.OfType<FacebookLiveCommentsSource>().FirstOrDefault();
            if (fbSource != null)
            {
                fbSource.SetPollInterval(TimeSpan.FromSeconds(30));
                Log.Logger.Information("Reduced Facebook poll interval to 30s due to health: {Failures} failures", health.ConsecutiveFailures);
            }
        }

        // Update UI tooltip with health info
        var status = platform switch
        {
            ChatPlatform.Twitch => TwitchStatus,
            ChatPlatform.YouTube => YouTubeStatus,
            ChatPlatform.Kick => KickStatus,
            ChatPlatform.Facebook => FacebookStatus,
            ChatPlatform.StreamerBot => StreamerBotStatus,
            _ => null
        };
        if (status is not null)
        {
            var healthDetail = health.ShouldReducePolling ? $" (degraded: {health.ConsecutiveFailures} failures)" : "";
            var tooltipText = status.ToolTip?.ToString() ?? "";
            var baseText = tooltipText.Split(':').LastOrDefault()?.Trim() ?? "";
            status.ToolTip = $"{platform}: {baseText}{healthDetail}";
        }
    }

    private void RegisterSource(IChatSource source, bool displayConnectionStatus = true)
    {
        source.ItemReceived += item => Dispatcher.Invoke(() => PublishItem(item));
        source.StateChanged += state => Dispatcher.Invoke(() =>
        {
            if (displayConnectionStatus)
                UpdateConnectionStatus(source.Platform, state);
            if (source.Platform == ChatPlatform.StreamerBot && _settings.ShowKick)
                SetConnectionStatusText(ChatPlatform.Kick, $"Kick via Streamer.bot: {state}");
        });
        source.ErrorOccurred += exception =>
        {
            Log.Logger.Warning(exception, "{Platform} chat source disconnected", source.Platform);
            Dispatcher.Invoke(() =>
            {
                var status = source.Platform switch
                {
                    ChatPlatform.Twitch => TwitchStatus,
                    ChatPlatform.YouTube => YouTubeStatus,
                    ChatPlatform.Kick => KickStatus,
                    ChatPlatform.Facebook => FacebookStatus,
                    ChatPlatform.StreamerBot => StreamerBotStatus,
                    _ => null
                };
                if (status is not null)
                    status.ToolTip = $"{source.Platform}: {exception.Message}";
                if (source.Platform == ChatPlatform.StreamerBot && _settings.ShowKick)
                    SetConnectionStatusText(ChatPlatform.Kick, $"Kick via Streamer.bot: {exception.Message}");
            });
        };
        _sources.Add(source);
    }

    private void InitializeConnectionStatus(string? twitchToken)
    {
        var needsTwitchAuth = _settings.ShowTwitch &&
                              (string.IsNullOrWhiteSpace(_settings.TwitchChannel) ||
                               string.IsNullOrWhiteSpace(_settings.TwitchUsername) ||
                               twitchToken is null);

        SetConnectionStatusText(
            ChatPlatform.Twitch,
            !_settings.ShowTwitch ? "Disabled in Settings" :
            needsTwitchAuth
                ? "Needs a channel and valid Twitch sign-in (OAuth required for chat)"
                : "Connecting...");
        SetConnectionStatusText(
            ChatPlatform.YouTube,
            !_settings.ShowYouTube ? "Disabled in Settings" :
            string.IsNullOrWhiteSpace(_settings.YouTubeTarget)
                ? "Needs a live video URL, video ID, or channel @handle"
                : "Connecting...");
        SetConnectionStatusText(
            ChatPlatform.Kick,
            !_settings.ShowKick ? "Disabled in Settings" :
            _settings.ShowStreamerBot ? "Waiting for Streamer.bot Kick events" :
            "Kick chat requires the Streamer.bot relay");
        SetConnectionStatusText(
            ChatPlatform.Facebook,
            !_settings.ShowFacebook ? "Disabled in Settings" :
            string.IsNullOrWhiteSpace(_settings.FacebookLiveVideoId) ||
            string.IsNullOrWhiteSpace(_settings.FacebookAccessTokenProtected)
                ? "Needs a live-video ID and Facebook access token"
                : "Connecting...");
        SetConnectionStatusText(
            ChatPlatform.StreamerBot,
            !_settings.ShowStreamerBot ? "Disabled in Settings" :
            Uri.TryCreate(_settings.StreamerBotUrl, UriKind.Absolute, out var uri) &&
            uri.Scheme is "ws" or "wss"
                ? "Connecting..."
                : "Needs a valid ws:// or wss:// URL");
    }

    private void SetConnectionStatusText(ChatPlatform platform, string detail)
    {
        var target = platform switch
        {
            ChatPlatform.Twitch => TwitchStatus,
            ChatPlatform.YouTube => YouTubeStatus,
            ChatPlatform.Kick => KickStatus,
            ChatPlatform.Facebook => FacebookStatus,
            ChatPlatform.StreamerBot => StreamerBotStatus,
            _ => null
        };
        if (target is not null)
            target.ToolTip = $"{platform}: {detail}";
    }

    private string? TryUnprotectToken(string protectedToken, ChatPlatform platform)
    {
        try
        {
            return SettingsStore.UnprotectToken(protectedToken);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            Log.Logger.Error(ex, "Unable to decrypt the saved {Platform} token for this Windows user", platform);
            UpdateConnectionStatus(platform, ConnectionState.Faulted);
            return null;
        }
    }

    private void PublishItem(ChatItem item)
    {
        var messageToRender = item.Message is { } receivedMessage
            ? _thirdPartyEmotes.Apply(receivedMessage)
            : null;
        if (messageToRender is { } message)
        {
            if ((_settings.DeduplicateMessages || _settings.DeduplicateStreamerBotMessages) &&
                !_deduplicator.ShouldAccept(message, item.Source, _settings.DeduplicateMessages,
                    _settings.DeduplicateStreamerBotMessages))
            {
                MultiChatEventSource.Log.MessageDeduplicated(message.Platform.ToString(), message.Id, "Deduplicated");
                return;
            }

            // Apply message filtering if enabled
            if (!ShouldShowMessage(message))
                return;

            if (!IsPlatformVisible(message.Platform))
                return;

            // Update last message time for auto-hide
            UpdateLastMessageTime();

            // ETW: Message received
            MultiChatEventSource.Log.MessageReceived(
                message.Platform.ToString(),
                message.Username,
                message.DisplayName,
                message.Id);
        }
        else if (item.Event is { } chatEvent && !IsPlatformVisible(chatEvent.Platform))
        {
            return;
        }

        if (item.Event is { } receivedEvent &&
            !_eventDeduplicator.ShouldAccept(receivedEvent, item.Source, _settings.DeduplicateStreamerBotEvents))
            return;

        // ETW: Event received
        if (item.Event is { } evt)
        {
            MultiChatEventSource.Log.EventReceived(
                evt.Platform.ToString(),
                evt.Type,
                evt.User,
                evt.Id);
        }

        // Log to chat export if enabled
        if (_chatLogExporter.IsEnabled)
        {
            if (messageToRender is { } msg)
                _chatLogExporter.LogMessage(msg);
            else if (item.Event is { } chatEvent)
                _chatLogExporter.LogEvent(chatEvent);
        }

        // Queue for batched delivery instead of immediate PostWebMessageAsJson
        QueueWebViewMessage(new { message = messageToRender, @event = item.Event });
    }

    private bool IsPlatformVisible(ChatPlatform platform) => platform switch
    {
        ChatPlatform.Twitch => _settings.ShowTwitch,
        ChatPlatform.YouTube => _settings.ShowYouTube,
        ChatPlatform.Kick => _settings.ShowKick,
        ChatPlatform.Facebook => _settings.ShowFacebook,
        ChatPlatform.StreamerBot => _settings.ShowStreamerBot,
        _ => true
    };

    private bool ShouldShowMessage(ChatMessage message)
    {
        if (!_settings.EnableMessageFiltering)
            return true;

        // Check ignored users (case-insensitive)
        if (_settings.IgnoredUsers.Count > 0)
        {
            var username = message.Username?.ToLowerInvariant() ?? string.Empty;
            if (_settings.IgnoredUsers.Any(u => u.Equals(username, StringComparison.OrdinalIgnoreCase)))
            {
                MultiChatEventSource.Log.MessageFiltered(message.Platform.ToString(), message.Username ?? string.Empty, "IgnoredUser");
                return false;
            }
        }

        var content = GetMessageText(message);

        // Check block patterns
        if (_settings.MessageBlockPatterns.Count > 0)
        {
            foreach (var pattern in _settings.MessageBlockPatterns)
            {
                try
                {
                    if (Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    {
                        MultiChatEventSource.Log.MessageFiltered(message.Platform.ToString(), message.Username ?? string.Empty, $"BlockPattern:{pattern}");
                        return false;
                    }
                }
                catch (ArgumentException ex)
                {
                    Log.Logger.Warning(ex, "Invalid block regex pattern: {Pattern}", pattern);
                }
            }
        }

        // Check allow patterns (if any are set, ONLY messages matching at least one are shown)
        if (_settings.MessageAllowPatterns.Count > 0)
        {
            var matched = false;
            foreach (var pattern in _settings.MessageAllowPatterns)
            {
                try
                {
                    if (Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    {
                        matched = true;
                        break;
                    }
                }
                catch (ArgumentException ex)
                {
                    Log.Logger.Warning(ex, "Invalid allow regex pattern: {Pattern}", pattern);
                }
            }

            if (!matched)
            {
                MultiChatEventSource.Log.MessageFiltered(message.Platform.ToString(), message.Username ?? string.Empty, "NoAllowMatch");
                return false;
            }
        }

        return true;
    }

    private static string GetMessageText(ChatMessage message)
    {
        var parts = new System.Text.StringBuilder();
        foreach (var part in message.MessageParts)
        {
            if (part is TextPart textPart)
                parts.Append(textPart.Text);
        }
        return parts.ToString();
    }

    private void UpdateConnectionStatus(ChatPlatform platform, ConnectionState state)
    {
        var color = state switch
        {
            ConnectionState.Connected => "#53FC18",
            ConnectionState.Connecting or ConnectionState.Reconnecting => "#FACC15",
            ConnectionState.Faulted => "#F87171",
            _ => "#8792A2"
        };
        var target = platform switch
        {
            ChatPlatform.Twitch => TwitchStatus,
            ChatPlatform.YouTube => YouTubeStatus,
            ChatPlatform.Kick => KickStatus,
            ChatPlatform.Facebook => FacebookStatus,
            ChatPlatform.StreamerBot => StreamerBotStatus,
            _ => null
        };
        if (target is not null)
        {
            target.Foreground = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(color)!;
            var detail = state switch
            {
                ConnectionState.Connected when platform == ChatPlatform.Twitch =>
                    $"Connected to #{_settings.TwitchChannel}",
                ConnectionState.Connected => "Connected",
                ConnectionState.Connecting => "Connecting",
                ConnectionState.Reconnecting => "Reconnecting",
                ConnectionState.Faulted => "Connection failed",
                _ => "Disconnected"
            };
            SetConnectionStatusText(platform, detail);
        }
    }

    private void SetupHotkeys()
    {
        var handle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);
        RegisterHotKey(handle, HotkeyLock, ModControl | ModShift, VkL);
        RegisterHotKey(handle, HotkeyVisibility, ModControl | ModShift, VkO);
        RegisterHotKey(handle, HotkeyClear, ModControl | ModShift, VkC);
        RegisterHotKey(handle, HotkeyOpacityUp, ModControl | ModShift, VkUp);
        RegisterHotKey(handle, HotkeyOpacityDown, ModControl | ModShift, VkDown);
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmShowOverlay)
        {
            ShowFromSecondInstance();
            handled = true;
            return IntPtr.Zero;
        }

        if (message != WmHotkey)
            return IntPtr.Zero;

        switch (wParam.ToInt32())
        {
            case HotkeyLock:
                SetLocked(!_settings.IsLocked);
                break;
            case HotkeyVisibility:
                ToggleVisibility();
                break;
            case HotkeyClear:
                QueueWebViewMessage(new { clear = true });
                break;
            case HotkeyOpacityUp:
                SetOpacity(Opacity + 0.05);
                break;
            case HotkeyOpacityDown:
                SetOpacity(Opacity - 0.05);
                break;
        }

        handled = true;
        return IntPtr.Zero;
    }

    private void ShowFromSecondInstance()
    {
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Topmost = false;
        Topmost = true;
        if (!_settings.IsLocked)
            Activate();
    }

    private void SetupTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Settings", null, async (_, _) => await OpenSettingsAsync());
        menu.Items.Add("Lock / Unlock", null, (_, _) => SetLocked(!_settings.IsLocked));
        menu.Items.Add("Show / Hide", null, (_, _) => ToggleVisibility());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Close());
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Multi Chat Overlay",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ToggleVisibility();
    }

    private void RestoreWindowBounds()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Clamp(_settings.Left, area.Left, Math.Max(area.Left, area.Right - 180));
        Top = Math.Clamp(_settings.Top, area.Top, Math.Max(area.Top, area.Bottom - 120));
        Width = Math.Clamp(_settings.Width, MinWidth, Math.Max(MinWidth, area.Width));
        Height = Math.Clamp(_settings.Height, MinHeight, Math.Max(MinHeight, area.Height));
    }

    private void SetLocked(bool isLocked, bool save = true)
    {
        _settings.IsLocked = isLocked;
        LockButton.Content = isLocked ? "●" : "○";
        LockButton.ToolTip = isLocked
            ? "Unlock overlay (Ctrl+Shift+L)"
            : "Lock overlay (Ctrl+Shift+L)";
        OverlayFrame.BorderBrush = isLocked
            ? System.Windows.Media.Brushes.Transparent
            : new SolidColorBrush(System.Windows.Media.Color.FromArgb(191, 255, 255, 255));
        Toolbar.Visibility = isLocked ? Visibility.Collapsed : Visibility.Visible;
        ToolbarRow.Height = isLocked ? new GridLength(0) : new GridLength(42);
        ResizeGrip.Visibility = isLocked ? Visibility.Collapsed : Visibility.Visible;
        ApplyExtendedStyles(isLocked);
        if (save)
            _ = SaveSettingsAsync();
    }

    private void ApplyExtendedStyles(bool isLocked)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        var style = GetWindowLongPtr(handle, GwlExstyle).ToInt64() | WsExLayered;
        style = isLocked ? style | WsExTransparent | WsExNoActivate : style & ~(WsExTransparent | WsExNoActivate);
        SetWindowLongPtr(handle, GwlExstyle, new IntPtr(style));
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
    }

    private void SetOpacity(double opacity)
    {
        Opacity = Math.Clamp(opacity, 0.25, 1);
        _settings.Opacity = Opacity;
        _ = SaveSettingsAsync();
    }

    private void MainMenu_Click(object sender, RoutedEventArgs e)
    {
        OverlayContextMenu.PlacementTarget = MainMenuButton;
        OverlayContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        OverlayContextMenu.IsOpen = true;
    }

    private void SetZoom_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { Tag: string factorText } &&
            double.TryParse(factorText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var factor) &&
            factor is >= 0.5 and <= 2)
            ChatView.ZoomFactor = factor;
    }

    private void IncreaseOpacity_Click(object sender, RoutedEventArgs e) => SetOpacity(Opacity + 0.05);
    private void DecreaseOpacity_Click(object sender, RoutedEventArgs e) => SetOpacity(Opacity - 0.05);
    private void ResetOpacity_Click(object sender, RoutedEventArgs e) => SetOpacity(1);
    private void ClearChat_Click(object sender, RoutedEventArgs e) => QueueWebViewMessage(new { clear = true });

    private void BringToTop_Click(object sender, RoutedEventArgs e)
    {
        Topmost = false;
        Topmost = true;
    }

    private void ToggleVisibility()
    {
        if (IsVisible)
            Hide();
        else
        {
            Show();
            if (!_settings.IsLocked)
                Activate();
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e) => await OpenSettingsAsync();
    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();
    private void Lock_Click(object sender, RoutedEventArgs e) => SetLocked(!_settings.IsLocked);
    private async Task OpenSettingsAsync()
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        dialog.CssPreviewChanged += PreviewCustomCss;
        if (dialog.ShowDialog() != true)
        {
            if (!string.IsNullOrWhiteSpace(_settings.CustomCss))
                await ApplyCustomCssAsync(_settings.CustomCss);
            if (!string.IsNullOrWhiteSpace(_settings.CustomCssPath))
                await LoadCustomCssFileAsync(_settings.CustomCssPath);
            return;
        }

        _settings = dialog.Settings;
        Opacity = _settings.Opacity;
        await SaveSettingsAsync();
        ConfigureCssWatcher();
        await StopSourcesAsync();
        await StartSourcesAsync();
        await ApplyCustomCssAsync(_settings.CustomCss);
        if (!string.IsNullOrWhiteSpace(_settings.CustomCssPath))
            await LoadCustomCssFileAsync(_settings.CustomCssPath);
    }

    private void PreviewCustomCss(string css)
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await ApplyCustomCssAsync(css);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Unable to update CSS preview");
            }
        });
    }

    private async Task ApplyCustomCssAsync(string css)
    {
        var encoded = JsonSerializer.Serialize(css);
        await ChatView.CoreWebView2.ExecuteScriptAsync($"window.overlay?.setCustomCss({encoded});");
    }

    private async Task LoadCustomCssFileAsync(string path)
    {
        try
        {
            var css = await File.ReadAllTextAsync(path);
            await ApplyCustomCssAsync(css);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Logger.Warning(ex, "Could not load custom CSS at {CssPath}", path);
        }
    }

    private void ConfigureCssWatcher()
    {
        _cssWatcher?.Dispose();
        _cssWatcher = null;
        if (string.IsNullOrWhiteSpace(_settings.CustomCssPath))
            return;

        try
        {
            var path = Path.GetFullPath(_settings.CustomCssPath);
            var directory = Path.GetDirectoryName(path);
            if (directory is null || !Directory.Exists(directory))
                return;

            _cssWatcher = new FileSystemWatcher(directory, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _cssWatcher.Changed += (_, _) => QueueCssReload(path);
            _cssWatcher.Created += (_, _) => QueueCssReload(path);
            _cssWatcher.Renamed += (_, _) => QueueCssReload(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Logger.Warning(ex, "Unable to watch custom CSS file {CssPath}", _settings.CustomCssPath);
        }
    }

    private void QueueCssReload(string path)
    {
        _cssReloadDelay?.Cancel();
        _cssReloadDelay?.Dispose();
        _cssReloadDelay = new CancellationTokenSource();
        var token = _cssReloadDelay.Token;
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await Task.Delay(250, token);
                await LoadCustomCssFileAsync(path);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        });
    }

    private async Task StopSourcesAsync()
    {
        foreach (var source in _sources)
        {
            try
            {
                await source.DisposeAsync();
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Unable to stop {Platform} source during shutdown", source.Platform);
            }
        }

        _sources.Clear();
    }

    private async Task SaveSettingsAsync()
    {
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Width = Width;
        _settings.Height = Height;
        _settings.Opacity = Opacity;
        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Logger.Error(ex, "Unable to save settings");
        }
    }

    private void Toolbar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            SetLocked(!_settings.IsLocked);
        else
            DragMove();
    }

    private void ResizeGrip_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_shutdownReady)
            return;

        e.Cancel = true;
        if (_shuttingDown)
            return;
        _shuttingDown = true;
        _ = CompleteShutdownAsync();
    }

    private async Task CompleteShutdownAsync()
    {
        try
        {
            _flushTimer?.Stop();
            _autoHideTimer?.Stop();
            await StopSourcesAsync();
            await SaveSettingsAsync();
            _chatLogExporter.Dispose(); // Stop chat log export
            HttpClientPool.Dispose(); // Release HTTP connection pools
            _cssWatcher?.Dispose();
            _cssReloadDelay?.Cancel();
            _cssReloadDelay?.Dispose();
            _trayIcon?.Dispose();
            var handle = new WindowInteropHelper(this).Handle;
            for (var id = HotkeyLock; id <= HotkeyOpacityDown; id++)
                UnregisterHotKey(handle, id);

            MultiChatEventSource.Log.ApplicationShuttingDown("User requested");
        }
        catch (Exception ex)
        {
            MultiChatEventSource.Log.UnhandledException(ex.GetType().Name, ex.Message, ex.StackTrace ?? "");
            Log.Logger.Error(ex, "Error while shutting down overlay services");
        }
        finally
        {
            try
            {
                ChatView.Dispose();
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Unable to dispose the WebView2 chat control");
            }

            _shutdownReady = true;
            Close();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}