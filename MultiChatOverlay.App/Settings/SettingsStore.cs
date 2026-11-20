using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace MultiChatOverlay.App.Settings;

public sealed class SettingsStore
{
    private readonly OAuthClientIdEnvStore _clientIdEnvStore;
    private readonly ProfileManager _profileManager;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string SettingsPath { get; }

    public SettingsStore()
    {
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MultiChatOverlay");
        SettingsPath = Path.Combine(settingsDirectory, "settings.json");
        _clientIdEnvStore = new OAuthClientIdEnvStore(settingsDirectory);
        _profileManager = new ProfileManager(this);
    }

    public ProfileManager ProfileManager => _profileManager;

    public async Task<OverlaySettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        // Try to load from profile system first
        var activeProfile = _profileManager.GetActiveProfile();
        if (activeProfile != null)
        {
            return ConvertProfileToOverlaySettings(activeProfile);
        }

        // Fallback to legacy settings.json
        OverlaySettings settings = new();
        if (File.Exists(SettingsPath))
        {
            await using var stream = File.OpenRead(SettingsPath);
            settings = await JsonSerializer.DeserializeAsync<OverlaySettings>(stream, JsonOptions, cancellationToken)
                       ?? throw new InvalidDataException("Settings file did not contain a settings object.");
        }

        var hasStoredClientIds =
            !string.IsNullOrWhiteSpace(settings.TwitchClientIdProtected) ||
            !string.IsNullOrWhiteSpace(settings.TwitchClientIdLegacy) ||
            !string.IsNullOrWhiteSpace(settings.GoogleClientIdProtected) ||
            !string.IsNullOrWhiteSpace(settings.GoogleClientIdLegacy) ||
            !string.IsNullOrWhiteSpace(settings.KickClientIdProtected) ||
            !string.IsNullOrWhiteSpace(settings.KickClientIdLegacy) ||
            !string.IsNullOrWhiteSpace(settings.FacebookAppIdProtected) ||
            !string.IsNullOrWhiteSpace(settings.FacebookAppIdLegacy);
        var hasLegacyGoogleClientSecret = !string.IsNullOrWhiteSpace(settings.GoogleClientSecretLegacy);
        await _clientIdEnvStore.LoadAsync(settings, cancellationToken);
        if (hasLegacyGoogleClientSecret)
        {
            settings.GoogleClientSecretProtected ??= ProtectToken(settings.GoogleClientSecretLegacy!);
            settings.GoogleClientSecretLegacy = null;
        }

        if (hasStoredClientIds || hasLegacyGoogleClientSecret)
            await SaveAsync(settings, cancellationToken);

        return settings;
    }

    public async Task SaveAsync(OverlaySettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporaryPath = SettingsPath + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, SettingsPath, true);
    }

    public async Task SaveProfileAsync(ProfileSettings profile, CancellationToken cancellationToken = default)
    {
        _profileManager.SaveProfile(profile);
        // Also update legacy settings for backward compatibility
        await SaveAsync(ConvertProfileToOverlaySettings(profile), cancellationToken);
    }

    public static OverlaySettings ConvertProfileToOverlaySettings(ProfileSettings profile)
    {
        return new OverlaySettings
        {
            ShowTwitch = profile.ShowTwitch,
            TwitchChannel = profile.TwitchChannel,
            TwitchUsername = profile.TwitchUsername,
            TwitchClientId = profile.TwitchClientId,
            TwitchClientIdProtected = profile.TwitchClientIdProtected,
            TwitchClientIdLegacy = profile.TwitchClientIdLegacy,
            TwitchOAuthTokenProtected = profile.TwitchOAuthTokenProtected,
            TwitchRefreshTokenProtected = profile.TwitchRefreshTokenProtected,
            UseTwitchAccountAsChannel = profile.UseTwitchAccountAsChannel,
            ShowYouTube = profile.ShowYouTube,
            YouTubeTarget = profile.YouTubeTarget,
            GoogleClientId = profile.GoogleClientId,
            GoogleClientIdProtected = profile.GoogleClientIdProtected,
            GoogleClientIdLegacy = profile.GoogleClientIdLegacy,
            GoogleClientSecretProtected = profile.GoogleClientSecretProtected,
            GoogleClientSecretLegacy = profile.GoogleClientSecretLegacy,
            YouTubeOAuthTokenProtected = profile.YouTubeOAuthTokenProtected,
            YouTubeRefreshTokenProtected = profile.YouTubeRefreshTokenProtected,
            ShowKick = profile.ShowKick,
            KickClientId = profile.KickClientId,
            KickClientIdProtected = profile.KickClientIdProtected,
            KickClientIdLegacy = profile.KickClientIdLegacy,
            KickClientSecretProtected = profile.KickClientSecretProtected,
            KickOAuthTokenProtected = profile.KickOAuthTokenProtected,
            KickRefreshTokenProtected = profile.KickRefreshTokenProtected,
            ShowFacebook = profile.ShowFacebook,
            FacebookAppId = profile.FacebookAppId,
            FacebookAppIdProtected = profile.FacebookAppIdProtected,
            FacebookAppIdLegacy = profile.FacebookAppIdLegacy,
            FacebookLiveVideoId = profile.FacebookLiveVideoId,
            FacebookAccessTokenProtected = profile.FacebookAccessTokenProtected,
            FacebookPollIntervalSeconds = profile.FacebookPollIntervalSeconds,
            FacebookMaxPollIntervalSeconds = profile.FacebookMaxPollIntervalSeconds,
            ShowStreamerBot = profile.ShowStreamerBot,
            StreamerBotUrl = profile.StreamerBotUrl,
            UseSevenTvEmotes = profile.UseSevenTvEmotes,
            UseBetterTtvEmotes = profile.UseBetterTtvEmotes,
            UseFrankerFaceZEmotes = profile.UseFrankerFaceZEmotes,
            DeduplicateMessages = profile.DeduplicateMessages,
            DeduplicateStreamerBotMessages = profile.DeduplicateStreamerBotMessages,
            DeduplicateStreamerBotEvents = profile.DeduplicateStreamerBotEvents,
            EnableChatLogExport = profile.EnableChatLogExport,
            ChatLogExportPath = profile.ChatLogExportPath,
            EnableMessageFiltering = profile.EnableMessageFiltering,
            MessageAllowPatterns = profile.MessageAllowPatterns,
            MessageBlockPatterns = profile.MessageBlockPatterns,
            IgnoredUsers = profile.IgnoredUsers,
            EnableAutoHide = profile.EnableAutoHide,
            AutoHideSeconds = profile.AutoHideSeconds,
            AutoHideOpacity = profile.AutoHideOpacity,
            Opacity = profile.Opacity,
            IsLocked = profile.IsLocked,
            CustomCss = profile.CustomCss,
            CustomCssPath = profile.CustomCssPath,
            CssThemePaths = profile.CssThemePaths,
            Left = profile.Left,
            Top = profile.Top,
            Width = profile.Width,
            Height = profile.Height
        };
    }

    public static ProfileSettings ConvertOverlaySettingsToProfile(OverlaySettings settings)
    {
        return new ProfileSettings
        {
            Name = "Default",
            IsActive = true,
            ShowTwitch = settings.ShowTwitch,
            TwitchChannel = settings.TwitchChannel,
            TwitchUsername = settings.TwitchUsername,
            TwitchClientId = settings.TwitchClientId,
            TwitchClientIdProtected = settings.TwitchClientIdProtected,
            TwitchClientIdLegacy = settings.TwitchClientIdLegacy,
            TwitchOAuthTokenProtected = settings.TwitchOAuthTokenProtected,
            TwitchRefreshTokenProtected = settings.TwitchRefreshTokenProtected,
            UseTwitchAccountAsChannel = settings.UseTwitchAccountAsChannel,
            ShowYouTube = settings.ShowYouTube,
            YouTubeTarget = settings.YouTubeTarget,
            GoogleClientId = settings.GoogleClientId,
            GoogleClientIdProtected = settings.GoogleClientIdProtected,
            GoogleClientIdLegacy = settings.GoogleClientIdLegacy,
            GoogleClientSecretProtected = settings.GoogleClientSecretProtected,
            GoogleClientSecretLegacy = settings.GoogleClientSecretLegacy,
            YouTubeOAuthTokenProtected = settings.YouTubeOAuthTokenProtected,
            YouTubeRefreshTokenProtected = settings.YouTubeRefreshTokenProtected,
            ShowKick = settings.ShowKick,
            KickClientId = settings.KickClientId,
            KickClientIdProtected = settings.KickClientIdProtected,
            KickClientIdLegacy = settings.KickClientIdLegacy,
            KickClientSecretProtected = settings.KickClientSecretProtected,
            KickOAuthTokenProtected = settings.KickOAuthTokenProtected,
            KickRefreshTokenProtected = settings.KickRefreshTokenProtected,
            ShowFacebook = settings.ShowFacebook,
            FacebookAppId = settings.FacebookAppId,
            FacebookAppIdProtected = settings.FacebookAppIdProtected,
            FacebookAppIdLegacy = settings.FacebookAppIdLegacy,
            FacebookLiveVideoId = settings.FacebookLiveVideoId,
            FacebookAccessTokenProtected = settings.FacebookAccessTokenProtected,
            FacebookPollIntervalSeconds = settings.FacebookPollIntervalSeconds,
            FacebookMaxPollIntervalSeconds = settings.FacebookMaxPollIntervalSeconds,
            ShowStreamerBot = settings.ShowStreamerBot,
            StreamerBotUrl = settings.StreamerBotUrl,
            UseSevenTvEmotes = settings.UseSevenTvEmotes,
            UseBetterTtvEmotes = settings.UseBetterTtvEmotes,
            UseFrankerFaceZEmotes = settings.UseFrankerFaceZEmotes,
            DeduplicateMessages = settings.DeduplicateMessages,
            DeduplicateStreamerBotMessages = settings.DeduplicateStreamerBotMessages,
            DeduplicateStreamerBotEvents = settings.DeduplicateStreamerBotEvents,
            EnableChatLogExport = settings.EnableChatLogExport,
            ChatLogExportPath = settings.ChatLogExportPath,
            EnableMessageFiltering = settings.EnableMessageFiltering,
            MessageAllowPatterns = settings.MessageAllowPatterns,
            MessageBlockPatterns = settings.MessageBlockPatterns,
            IgnoredUsers = settings.IgnoredUsers,
            EnableAutoHide = settings.EnableAutoHide,
            AutoHideSeconds = settings.AutoHideSeconds,
            AutoHideOpacity = settings.AutoHideOpacity,
            Opacity = settings.Opacity,
            IsLocked = settings.IsLocked,
            CustomCss = settings.CustomCss,
            CustomCssPath = settings.CustomCssPath,
            CssThemePaths = settings.CssThemePaths,
            Left = settings.Left,
            Top = settings.Top,
            Width = settings.Width,
            Height = settings.Height
        };
    }

    public static string ProtectToken(string token)
    {
        if (string.IsNullOrEmpty(token))
            return string.Empty;

        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string UnprotectToken(string protectedToken)
    {
        var encrypted = Convert.FromBase64String(protectedToken);
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
    }

}
