using System.Text.Json.Serialization;

namespace MultiChatOverlay.App.Settings;

public sealed class OverlaySettings
{
    public double Left { get; set; } = 40;
    public double Top { get; set; } = 80;
    public double Width { get; set; } = 420;
    public double Height { get; set; } = 760;
    public double Opacity { get; set; } = 0.96;
    public bool IsLocked { get; set; }
    public bool ShowTwitch { get; set; } = true;
    public bool ShowYouTube { get; set; } = true;
    public bool ShowKick { get; set; } = true;
    public bool ShowFacebook { get; set; } = true;
    public bool ShowStreamerBot { get; set; } = true;
    public bool DeduplicateMessages { get; set; } = true;
    public bool UseSevenTvEmotes { get; set; } = true;
    public bool UseBetterTtvEmotes { get; set; } = true;
    public bool UseFrankerFaceZEmotes { get; set; } = true;
    public bool UseTwitchAccountAsChannel { get; set; } = true;
    public string TwitchChannel { get; set; } = string.Empty;
    public string TwitchUsername { get; set; } = string.Empty;
    [JsonIgnore]
    public string TwitchClientId { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TwitchClientIdProtected { get; set; }
    [JsonPropertyName("twitchClientId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TwitchClientIdLegacy { get; set; }
    public string StreamerBotUrl { get; set; } = "ws://127.0.0.1:8080/";
    public string? TwitchOAuthTokenProtected { get; set; }
    public string? TwitchRefreshTokenProtected { get; set; }
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
    [JsonIgnore]
    public string FacebookAppId { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FacebookAppIdProtected { get; set; }
    [JsonPropertyName("facebookAppId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FacebookAppIdLegacy { get; set; }
    public string FacebookLiveVideoId { get; set; } = string.Empty;
    public string? FacebookAccessTokenProtected { get; set; }
    public string? CustomCssPath { get; set; }
    public string CustomCss { get; set; } = string.Empty;
    public List<string> CssThemePaths { get; set; } = [];
}
