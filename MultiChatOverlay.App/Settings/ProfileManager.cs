using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MultiChatOverlay.App.Logging;

namespace MultiChatOverlay.App.Settings;

public sealed class ProfileManager
{
    private readonly string _profilesDir;
    private readonly SettingsStore _settingsStore;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ProfileManager(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        _profilesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MultiChatOverlay",
            "profiles");
        Directory.CreateDirectory(_profilesDir);
    }

    public List<ProfileSettings> GetAllProfiles()
    {
        var profiles = new List<ProfileSettings>();
        try
        {
            foreach (var file in Directory.GetFiles(_profilesDir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var profile = JsonSerializer.Deserialize<ProfileSettings>(json, _jsonOptions);
                    if (profile != null)
                        profiles.Add(profile);
                }
                catch (Exception ex)
                {
                    Log.Logger.Warning(ex, "Failed to load profile from {ProfileFile}", file);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "Failed to enumerate profiles directory");
        }

        // Ensure at least one profile exists
        if (profiles.Count == 0)
        {
            var defaultProfile = new ProfileSettings { Name = "Default", IsActive = true };
            profiles.Add(defaultProfile);
            SaveProfile(defaultProfile);
        }

        return profiles.OrderBy(p => p.Name).ToList();
    }

    public ProfileSettings? GetActiveProfile()
    {
        return GetAllProfiles().FirstOrDefault(p => p.IsActive);
    }

    public ProfileSettings GetOrCreateDefaultProfile()
    {
        var active = GetActiveProfile();
        if (active != null)
            return active;

        var profiles = GetAllProfiles();
        if (profiles.Count > 0)
        {
            profiles[0].IsActive = true;
            SaveProfile(profiles[0]);
            return profiles[0];
        }

        var defaultProfile = new ProfileSettings { Name = "Default", IsActive = true };
        SaveProfile(defaultProfile);
        return defaultProfile;
    }

    public void SaveProfile(ProfileSettings profile)
    {
        try
        {
            var fileName = SanitizeFileName(profile.Name) + ".json";
            var filePath = Path.Combine(_profilesDir, fileName);
            var json = JsonSerializer.Serialize(profile, _jsonOptions);
            File.WriteAllText(filePath, json);
            Log.Logger.Information("Saved profile: {ProfileName}", profile.Name);
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Failed to save profile {ProfileName}", profile.Name);
        }
    }

    public void DeleteProfile(string profileName)
    {
        if (profileName == "Default")
            return; // Prevent deleting default profile

        try
        {
            var fileName = SanitizeFileName(profileName) + ".json";
            var filePath = Path.Combine(_profilesDir, fileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Log.Logger.Information("Deleted profile: {ProfileName}", profileName);
            }
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Failed to delete profile {ProfileName}", profileName);
        }
    }

    public ProfileSettings CreateProfile(string name, ProfileSettings? baseProfile = null)
    {
        var profile = baseProfile?.Clone() ?? new ProfileSettings();
        profile.Name = GetUniqueProfileName(name);
        profile.IsActive = false;
        SaveProfile(profile);
        return profile;
    }

    public void SetActiveProfile(string profileName)
    {
        var profiles = GetAllProfiles();
        foreach (var profile in profiles)
        {
            profile.IsActive = profile.Name == profileName;
            SaveProfile(profile);
        }
    }

    public void MigrateFromLegacySettings(OverlaySettings legacySettings)
    {
        var profile = new ProfileSettings
        {
            Name = "Migrated",
            IsActive = true,
            ShowTwitch = legacySettings.ShowTwitch,
            TwitchChannel = legacySettings.TwitchChannel,
            TwitchUsername = legacySettings.TwitchUsername,
            TwitchClientId = legacySettings.TwitchClientId,
            TwitchClientIdProtected = legacySettings.TwitchClientIdProtected,
            TwitchOAuthTokenProtected = legacySettings.TwitchOAuthTokenProtected,
            TwitchRefreshTokenProtected = legacySettings.TwitchRefreshTokenProtected,
            UseTwitchAccountAsChannel = legacySettings.UseTwitchAccountAsChannel,
            ShowYouTube = legacySettings.ShowYouTube,
            YouTubeTarget = legacySettings.YouTubeTarget,
            GoogleClientId = legacySettings.GoogleClientId,
            GoogleClientIdProtected = legacySettings.GoogleClientIdProtected,
            GoogleClientSecretProtected = legacySettings.GoogleClientSecretProtected,
            YouTubeOAuthTokenProtected = legacySettings.YouTubeOAuthTokenProtected,
            YouTubeRefreshTokenProtected = legacySettings.YouTubeRefreshTokenProtected,
            ShowKick = legacySettings.ShowKick,
            KickClientId = legacySettings.KickClientId,
            KickClientIdProtected = legacySettings.KickClientIdProtected,
            KickClientSecretProtected = legacySettings.KickClientSecretProtected,
            KickOAuthTokenProtected = legacySettings.KickOAuthTokenProtected,
            KickRefreshTokenProtected = legacySettings.KickRefreshTokenProtected,
            ShowFacebook = legacySettings.ShowFacebook,
            FacebookAppId = legacySettings.FacebookAppId,
            FacebookAppIdProtected = legacySettings.FacebookAppIdProtected,
            FacebookLiveVideoId = legacySettings.FacebookLiveVideoId,
            FacebookAccessTokenProtected = legacySettings.FacebookAccessTokenProtected,
            FacebookPollIntervalSeconds = legacySettings.FacebookPollIntervalSeconds,
            FacebookMaxPollIntervalSeconds = legacySettings.FacebookMaxPollIntervalSeconds,
            ShowStreamerBot = legacySettings.ShowStreamerBot,
            StreamerBotUrl = legacySettings.StreamerBotUrl,
            UseSevenTvEmotes = legacySettings.UseSevenTvEmotes,
            UseBetterTtvEmotes = legacySettings.UseBetterTtvEmotes,
            UseFrankerFaceZEmotes = legacySettings.UseFrankerFaceZEmotes,
            DeduplicateMessages = legacySettings.DeduplicateMessages,
            DeduplicateStreamerBotMessages = legacySettings.DeduplicateStreamerBotMessages,
            DeduplicateStreamerBotEvents = legacySettings.DeduplicateStreamerBotEvents,
            EnableChatLogExport = legacySettings.EnableChatLogExport,
            ChatLogExportPath = legacySettings.ChatLogExportPath,
            EnableMessageFiltering = legacySettings.EnableMessageFiltering,
            MessageAllowPatterns = legacySettings.MessageAllowPatterns,
            MessageBlockPatterns = legacySettings.MessageBlockPatterns,
            IgnoredUsers = legacySettings.IgnoredUsers,
            EnableAutoHide = legacySettings.EnableAutoHide,
            AutoHideSeconds = legacySettings.AutoHideSeconds,
            AutoHideOpacity = legacySettings.AutoHideOpacity,
            Opacity = legacySettings.Opacity,
            IsLocked = legacySettings.IsLocked,
            CustomCss = legacySettings.CustomCss,
            CustomCssPath = legacySettings.CustomCssPath,
            CssThemePaths = legacySettings.CssThemePaths,
            Left = legacySettings.Left,
            Top = legacySettings.Top,
            Width = legacySettings.Width,
            Height = legacySettings.Height
        };

        // Save as new profile
        SaveProfile(profile);

        // Also save to legacy settings file for backward compatibility
        _settingsStore.SaveAsync(legacySettings).Wait();
    }

    private static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
        return sanitized.Length > 50 ? sanitized[..50] : sanitized;
    }

    private string GetUniqueProfileName(string baseName)
    {
        var profiles = GetAllProfiles();
        var existingNames = new HashSet<string>(profiles.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        var name = baseName;
        var counter = 1;
        while (existingNames.Contains(name))
        {
            name = $"{baseName} ({counter})";
            counter++;
        }
        return name;
    }
}