using System.IO;
using System.Security.Cryptography;

namespace MultiChatOverlay.App.Settings;

public sealed class OAuthClientIdEnvStore(string settingsDirectory)
{
    private static readonly string[] Keys =
    [
        "TWITCH_CLIENT_ID",
        "GOOGLE_CLIENT_ID",
        "KICK_CLIENT_ID",
        "FACEBOOK_APP_ID"
    ];

    public string EnvPath { get; } = Path.Combine(settingsDirectory, ".env");

    public async Task LoadAsync(OverlaySettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var envExists = File.Exists(EnvPath);
        var values = envExists
            ? Parse(await File.ReadAllLinesAsync(EnvPath, cancellationToken))
            : new Dictionary<string, string>(StringComparer.Ordinal);
        var hasMissingKeys = Keys.Any(key => !values.ContainsKey(key));

        AddMissingValue("TWITCH_CLIENT_ID", settings.TwitchClientIdProtected, settings.TwitchClientIdLegacy);
        AddMissingValue("GOOGLE_CLIENT_ID", settings.GoogleClientIdProtected, settings.GoogleClientIdLegacy);
        AddMissingValue("KICK_CLIENT_ID", settings.KickClientIdProtected, settings.KickClientIdLegacy);
        AddMissingValue("FACEBOOK_APP_ID", settings.FacebookAppIdProtected, settings.FacebookAppIdLegacy);

        var requiresEnvWrite = !envExists || hasMissingKeys;
        if (requiresEnvWrite)
            await SaveAsync(values, cancellationToken);

        settings.TwitchClientId = values["TWITCH_CLIENT_ID"];
        settings.GoogleClientId = values["GOOGLE_CLIENT_ID"];
        settings.KickClientId = values["KICK_CLIENT_ID"];
        settings.FacebookAppId = values["FACEBOOK_APP_ID"];

        settings.TwitchClientIdProtected = null;
        settings.TwitchClientIdLegacy = null;
        settings.GoogleClientIdProtected = null;
        settings.GoogleClientIdLegacy = null;
        settings.KickClientIdProtected = null;
        settings.KickClientIdLegacy = null;
        settings.FacebookAppIdProtected = null;
        settings.FacebookAppIdLegacy = null;

        void AddMissingValue(string key, string? protectedValue, string? legacyValue)
        {
            if (!values.ContainsKey(key))
                values[key] = GetSavedValue(protectedValue, legacyValue);
        }
    }

    private async Task SaveAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(EnvPath)!);
        var content = string.Join(
            Environment.NewLine,
            Keys.Select(key => $"{key}={Escape(values.GetValueOrDefault(key) ?? string.Empty)}"));
        var temporaryPath = EnvPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, content + Environment.NewLine, cancellationToken);
        File.Move(temporaryPath, EnvPath, true);
    }

    private static Dictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var knownKeys = new HashSet<string>(Keys, StringComparer.Ordinal);
        var lineNumber = 0;
        foreach (var line in lines)
        {
            lineNumber++;
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
                throw new InvalidDataException($"Invalid OAuth .env entry on line {lineNumber}.");

            var key = trimmed[..separator].Trim();
            if (!knownKeys.Contains(key))
                throw new InvalidDataException($"Unsupported key '{key}' in the OAuth .env file.");

            var value = trimmed[(separator + 1)..].Trim();
            if (value.Length >= 2 && value[0] == value[^1] && value[0] is '"' or '\'')
                value = value[1..^1];
            else if (value.StartsWith('"') || value.StartsWith('\'') || value.EndsWith('"') || value.EndsWith('\''))
                throw new InvalidDataException($"Unterminated quoted value for {key} in the OAuth .env file.");

            values[key] = value;
        }

        return values;
    }

    private static string GetSavedValue(string? protectedValue, string? legacyValue)
    {
        if (!string.IsNullOrWhiteSpace(protectedValue))
        {
            try
            {
                return SettingsStore.UnprotectToken(protectedValue);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                throw new InvalidDataException(
                    "A saved OAuth Client ID cannot be decrypted. Restore the original Windows-user settings or add the Client ID to the .env file.",
                    ex);
            }
        }

        return legacyValue?.Trim() ?? string.Empty;
    }

    private static string Escape(string value)
    {
        if (value.Contains('\r') || value.Contains('\n'))
            throw new InvalidDataException("OAuth Client IDs cannot contain newline characters.");

        return value;
    }
}
