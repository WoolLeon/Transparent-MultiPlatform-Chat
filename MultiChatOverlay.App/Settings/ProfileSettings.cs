using System.Text.Json.Serialization;

namespace MultiChatOverlay.App.Settings;

public sealed class ProfileSettings
{
    public string Name { get; set; } = "Default";
    public bool IsActive { get; set; }

    // Twitch
    public bool ShowTwitch { get; set; } = true;
    public string TwitchChannel { get; set; } = string.Empty;
    public string TwitchUsername { get; set; } = string.Empty;
    public string? TwitchClientId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TwitchClientIdProtected { get; set; }
    [JsonPropertyName("twitchClientId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TwitchClientIdLegacy { get; set; }
    public string? TwitchOAuthTokenProtected { get; set; }
    public string? TwitchRefreshTokenProtected { get; set; }
    public bool UseTwitchAccountAsChannel { get; set; } = true;

    // YouTube
    public bool ShowYouTube { get; set; } = true;
    [JsonPropertyName("youTubeLiveChatId")]
    public string YouTubeTarget { get; set; } = string.Empty;
    [JsonIgnore]
    public string GoogleClientId { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GoogleClientIdProtected { get; set; }
    [JsonPropertyName("googleClientId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GoogleClientIdLegacy { get; set; }
    public string? GoogleClientSecretProtected { get; set; }
    [JsonPropertyName("googleClientSecret")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GoogleClientSecretLegacy { get; set; }
    public string? YouTubeOAuthTokenProtected { get; set; }
    public string? YouTubeRefreshTokenProtected { get; set; }

    // Kick
    public bool ShowKick { get; set; } = true;
    [JsonIgnore]
    public string KickClientId { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? KickClientIdProtected { get; set; }
    [JsonPropertyName("kickClientId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? KickClientIdLegacy { get; set; }
    public string? KickClientSecretProtected { get; set; }
    public string? KickOAuthTokenProtected { get; set; }
    public string? KickRefreshTokenProtected { get; set; }

    // Facebook
    public bool ShowFacebook { get; set; } = true;
    [JsonIgnore]
    public string FacebookAppId { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FacebookAppIdProtected { get; set; }
    [JsonPropertyName("facebookAppId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FacebookAppIdLegacy { get; set; }
    public string FacebookLiveVideoId { get; set; } = string.Empty;
    public string? FacebookAccessTokenProtected { get; set; }
    public int FacebookPollIntervalSeconds { get; set; } = 5;
    public int FacebookMaxPollIntervalSeconds { get; set; } = 60;

    // Streamer.bot
    public bool ShowStreamerBot { get; set; } = true;
    public string StreamerBotUrl { get; set; } = "ws://127.0.0.1:8080/";

    // Emotes
    public bool UseSevenTvEmotes { get; set; } = true;
    public bool UseBetterTtvEmotes { get; set; } = true;
    public bool UseFrankerFaceZEmotes { get; set; } = true;

    // Deduplication
    public bool DeduplicateMessages { get; set; } = true;
    public bool DeduplicateStreamerBotMessages { get; set; } = true;
    public bool DeduplicateStreamerBotEvents { get; set; } = true;

    // Chat log export
    public bool EnableChatLogExport { get; set; } = true;
    public string? ChatLogExportPath { get; set; }

    // Message filtering
    public bool EnableMessageFiltering { get; set; } = false;
    public List<string> MessageAllowPatterns { get; set; } = [];
    public List<string> MessageBlockPatterns { get; set; } = [];
    public List<string> IgnoredUsers { get; set; } = [];

    // Auto-hide
    public bool EnableAutoHide { get; set; } = false;
    public int AutoHideSeconds { get; set; } = 30;
    public double AutoHideOpacity { get; set; } = 0.1;

    // Ensure AutoHideOpacity never goes below 0.05
    public double SafeAutoHideOpacity => Math.Clamp(AutoHideOpacity, 0.05, 0.5);

    // Appearance
    public double Opacity { get; set; } = 0.96;
    public bool IsLocked { get; set; }
    public string CustomCss { get; set; } = string.Empty;
    public string? CustomCssPath { get; set; }
    public List<string> CssThemePaths { get; set; } = [];

    // Window position
    public double Left { get; set; } = 40;
    public double Top { get; set; } = 80;
    public double Width { get; set; } = 420;
    public double Height { get; set; } = 760;

    public ProfileSettings Clone()
    {
        return (ProfileSettings)MemberwiseClone();
    }
}