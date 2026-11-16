using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace MultiChatOverlay.App.Settings;

public sealed class SettingsStore
{
    private readonly OAuthClientIdEnvStore _clientIdEnvStore;
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
    }

    public async Task<OverlaySettings> LoadAsync(CancellationToken cancellationToken = default)
    {
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
