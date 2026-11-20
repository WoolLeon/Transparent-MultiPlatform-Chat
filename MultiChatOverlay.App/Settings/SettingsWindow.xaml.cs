using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.IO;
using Microsoft.Web.WebView2.Core;
using MultiChatOverlay.Core.Models;

namespace MultiChatOverlay.App.Settings;

public partial class SettingsWindow : Window
{
    private static readonly JsonSerializerOptions PreviewJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public OverlaySettings Settings { get; private set; }
    public event Action<string>? CssPreviewChanged;
    private readonly string _twitchClientId;
    private readonly string _kickClientId;
    private readonly string _facebookAppId;
    private int _twitchAvatarRequestId;
    private bool _isLoadingCss;
    private bool _cssPreviewReady;
    private bool _cssPreviewSamplesPublished;
    private readonly ProfileManager _profileManager;
    private bool _isLoadingProfiles;

    public SettingsWindow(OverlaySettings settings, ProfileManager? profileManager = null)
    {
        InitializeComponent();
        _profileManager = profileManager ?? new ProfileManager(new SettingsStore());

        Settings = new OverlaySettings
        {
            Left = settings.Left,
            Top = settings.Top,
            Width = settings.Width,
            Height = settings.Height,
            Opacity = settings.Opacity,
            IsLocked = settings.IsLocked,
            ShowTwitch = settings.ShowTwitch,
            ShowYouTube = settings.ShowYouTube,
            ShowKick = settings.ShowKick,
            ShowFacebook = settings.ShowFacebook,
            ShowStreamerBot = settings.ShowStreamerBot,
            DeduplicateMessages = settings.DeduplicateMessages,
            DeduplicateStreamerBotMessages = settings.DeduplicateStreamerBotMessages,
            DeduplicateStreamerBotEvents = settings.DeduplicateStreamerBotEvents,
            UseSevenTvEmotes = settings.UseSevenTvEmotes,
            UseBetterTtvEmotes = settings.UseBetterTtvEmotes,
            UseFrankerFaceZEmotes = settings.UseFrankerFaceZEmotes,
            UseTwitchAccountAsChannel = settings.UseTwitchAccountAsChannel,
            TwitchChannel = settings.TwitchChannel,
            TwitchUsername = settings.TwitchUsername,
            TwitchClientId = settings.TwitchClientId,
            StreamerBotUrl = settings.StreamerBotUrl,
            TwitchOAuthTokenProtected = settings.TwitchOAuthTokenProtected,
            TwitchRefreshTokenProtected = settings.TwitchRefreshTokenProtected,
            YouTubeTarget = settings.YouTubeTarget,
            KickClientId = settings.KickClientId,
            KickClientSecretProtected = settings.KickClientSecretProtected,
            KickOAuthTokenProtected = settings.KickOAuthTokenProtected,
            KickRefreshTokenProtected = settings.KickRefreshTokenProtected,
            FacebookAppId = settings.FacebookAppId,
            FacebookLiveVideoId = settings.FacebookLiveVideoId,
            FacebookAccessTokenProtected = settings.FacebookAccessTokenProtected,
            CustomCssPath = settings.CustomCssPath,
            CustomCss = settings.CustomCss,
            CssThemePaths = [.. settings.CssThemePaths],
            EnableMessageFiltering = settings.EnableMessageFiltering,
            MessageAllowPatterns = settings.MessageAllowPatterns,
            MessageBlockPatterns = settings.MessageBlockPatterns,
            IgnoredUsers = settings.IgnoredUsers
        };

        LoadProfiles();
        OpacitySlider.Value = Settings.Opacity;
        DedupeCheckBox.IsChecked = Settings.DeduplicateMessages;
        StreamerBotMessageDedupe.IsChecked = Settings.DeduplicateStreamerBotMessages;
        StreamerBotEventDedupe.IsChecked = Settings.DeduplicateStreamerBotEvents;
        SevenTvEmotesEnabled.IsChecked = Settings.UseSevenTvEmotes;
        BetterTtvEmotesEnabled.IsChecked = Settings.UseBetterTtvEmotes;
        FrankerFaceZEmotesEnabled.IsChecked = Settings.UseFrankerFaceZEmotes;
        TwitchEnabled.IsChecked = Settings.ShowTwitch;
        UseTwitchAccountAsChannelCheckBox.IsChecked = Settings.UseTwitchAccountAsChannel;
        TwitchChannelBox.Text = Settings.UseTwitchAccountAsChannel && !string.IsNullOrWhiteSpace(Settings.TwitchUsername)
            ? Settings.TwitchUsername
            : Settings.TwitchChannel;
        TwitchChannelBox.IsEnabled = !Settings.UseTwitchAccountAsChannel;
        _twitchClientId = Settings.TwitchClientId;
        SetTwitchAccountSignedIn(!string.IsNullOrWhiteSpace(Settings.TwitchOAuthTokenProtected));
        if (!string.IsNullOrWhiteSpace(Settings.TwitchOAuthTokenProtected))
            _ = LoadTwitchAvatarAsync();
        YouTubeTargetBox.Text = Settings.YouTubeTarget;
        FacebookLiveVideoIdBox.Text = Settings.FacebookLiveVideoId;
        _kickClientId = Settings.KickClientId;
        KickAccountStatus.Text = string.IsNullOrWhiteSpace(Settings.KickOAuthTokenProtected)
            ? "Not signed in"
            : "Signed in";
        _facebookAppId = Settings.FacebookAppId;
        FacebookLiveVideoIdBox.Text = Settings.FacebookLiveVideoId;
        FacebookPollIntervalBox.Text = Settings.FacebookPollIntervalSeconds.ToString();
        FacebookMaxPollIntervalBox.Text = Settings.FacebookMaxPollIntervalSeconds.ToString();
        FacebookAccountStatus.Text = string.IsNullOrWhiteSpace(Settings.FacebookAccessTokenProtected)
            ? "Not signed in"
            : "Signed in";
        YouTubeEnabled.IsChecked = Settings.ShowYouTube;
        KickEnabled.IsChecked = Settings.ShowKick;
        FacebookEnabled.IsChecked = Settings.ShowFacebook;
        StreamerBotEnabled.IsChecked = Settings.ShowStreamerBot;
        StreamerBotUrlBox.Text = Settings.StreamerBotUrl;
        EnableAutoHideCheckBox.IsChecked = Settings.EnableAutoHide;
        AutoHideSecondsBox.Text = Settings.AutoHideSeconds.ToString();
        AutoHideOpacitySlider.Value = Settings.AutoHideOpacity;
        EnableMessageFilteringCheckBox.IsChecked = Settings.EnableMessageFiltering;
        AllowPatternsBox.Text = string.Join(Environment.NewLine, Settings.MessageAllowPatterns);
        BlockPatternsBox.Text = string.Join(Environment.NewLine, Settings.MessageBlockPatterns);
        IgnoredUsersBox.Text = string.Join(Environment.NewLine, Settings.IgnoredUsers);
        _isLoadingCss = true;
        CssPathBox.Text = Settings.CustomCssPath ?? string.Empty;
        CustomCssBox.Text = Settings.CustomCss;
        _isLoadingCss = false;
        foreach (var path in Settings.CssThemePaths.Where(File.Exists))
            AddCssThemeOption(path);
        if (!string.IsNullOrWhiteSpace(Settings.CustomCssPath) && File.Exists(Settings.CustomCssPath))
        {
            var selectedTheme = FindCssThemeOption(Settings.CustomCssPath) ?? AddCssThemeOption(Settings.CustomCssPath);
            ThemePicker.SelectedItem = selectedTheme;
        }
    }

    private void LoadProfiles()
    {
        _isLoadingProfiles = true;
        try
        {
            var profiles = _profileManager.GetAllProfiles();
            ProfileSelector.Items.Clear();
            foreach (var profile in profiles)
            {
                var item = new ComboBoxItem
                {
                    Content = profile.Name,
                    Tag = profile,
                    IsSelected = profile.IsActive
                };
                ProfileSelector.Items.Add(item);
            }
            UpdateProfileInfo();
        }
        finally
        {
            _isLoadingProfiles = false;
        }
    }

    private void ProfileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingProfiles || ProfileSelector.SelectedItem is not ComboBoxItem selectedItem)
            return;

        var selectedProfile = selectedItem.Tag as ProfileSettings;
        if (selectedProfile == null)
            return;

        // Save current settings to the previously active profile
        var activeProfile = _profileManager.GetActiveProfile();
        if (activeProfile != null && activeProfile.Name != selectedProfile.Name)
        {
            SaveCurrentSettingsToProfile(activeProfile);
        }

        // Switch to the selected profile
        _profileManager.SetActiveProfile(selectedProfile.Name);
        LoadProfileSettings(selectedProfile);
        UpdateProfileInfo();
    }

    private void SaveCurrentSettingsToProfile(ProfileSettings profile)
    {
        profile.Opacity = OpacitySlider.Value;
        profile.DeduplicateMessages = DedupeCheckBox.IsChecked == true;
        profile.DeduplicateStreamerBotMessages = StreamerBotMessageDedupe.IsChecked == true;
        profile.DeduplicateStreamerBotEvents = StreamerBotEventDedupe.IsChecked == true;
        profile.UseSevenTvEmotes = SevenTvEmotesEnabled.IsChecked == true;
        profile.UseBetterTtvEmotes = BetterTtvEmotesEnabled.IsChecked == true;
        profile.UseFrankerFaceZEmotes = FrankerFaceZEmotesEnabled.IsChecked == true;
        profile.UseTwitchAccountAsChannel = UseTwitchAccountAsChannelCheckBox.IsChecked == true;
        profile.ShowTwitch = TwitchEnabled.IsChecked == true;
        profile.TwitchChannel = TwitchChannelBox.Text.Trim().TrimStart('#');
        profile.ShowYouTube = YouTubeEnabled.IsChecked == true;
        profile.YouTubeTarget = YouTubeTargetBox.Text.Trim();
        profile.ShowKick = KickEnabled.IsChecked == true;
        profile.ShowFacebook = FacebookEnabled.IsChecked == true;
        profile.FacebookLiveVideoId = FacebookLiveVideoIdBox.Text.Trim();
        profile.FacebookPollIntervalSeconds = int.TryParse(FacebookPollIntervalBox.Text, out var pollInterval) ? Math.Clamp(pollInterval, 1, 300) : 5;
        profile.FacebookMaxPollIntervalSeconds = int.TryParse(FacebookMaxPollIntervalBox.Text, out var maxPollInterval) ? Math.Clamp(maxPollInterval, 5, 3600) : 60;
        profile.ShowStreamerBot = StreamerBotEnabled.IsChecked == true;
        profile.StreamerBotUrl = StreamerBotUrlBox.Text.Trim();
        profile.CustomCssPath = string.IsNullOrWhiteSpace(CssPathBox.Text) ? null : CssPathBox.Text.Trim();
        profile.CustomCss = CustomCssBox.Text;
        profile.CssThemePaths = [.. Settings.CssThemePaths.Distinct(StringComparer.OrdinalIgnoreCase)];
        profile.EnableAutoHide = EnableAutoHideCheckBox.IsChecked == true;
        profile.AutoHideSeconds = int.TryParse(AutoHideSecondsBox.Text, out var autoHideSec) ? Math.Clamp(autoHideSec, 5, 3600) : 30;
        profile.AutoHideOpacity = Math.Clamp(AutoHideOpacitySlider.Value, 0.05, 0.5);
        profile.EnableMessageFiltering = EnableMessageFilteringCheckBox.IsChecked == true;
        profile.MessageAllowPatterns = AllowPatternsBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        profile.MessageBlockPatterns = BlockPatternsBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        profile.IgnoredUsers = IgnoredUsersBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        profile.TwitchClientId = _twitchClientId;
        profile.TwitchOAuthTokenProtected = Settings.TwitchOAuthTokenProtected;
        profile.TwitchRefreshTokenProtected = Settings.TwitchRefreshTokenProtected;
        profile.TwitchUsername = Settings.TwitchUsername;
        profile.KickClientId = _kickClientId;
        profile.KickClientSecretProtected = Settings.KickClientSecretProtected;
        profile.KickOAuthTokenProtected = Settings.KickOAuthTokenProtected;
        profile.KickRefreshTokenProtected = Settings.KickRefreshTokenProtected;
        profile.FacebookAppId = _facebookAppId;
        profile.FacebookAccessTokenProtected = Settings.FacebookAccessTokenProtected;

        _profileManager.SaveProfile(profile);
    }

    private void LoadProfileSettings(ProfileSettings profile)
    {
        _isLoadingProfiles = true;
        try
        {
            Settings = SettingsStore.ConvertProfileToOverlaySettings(profile);

            OpacitySlider.Value = profile.Opacity;
            DedupeCheckBox.IsChecked = profile.DeduplicateMessages;
            StreamerBotMessageDedupe.IsChecked = profile.DeduplicateStreamerBotMessages;
            StreamerBotEventDedupe.IsChecked = profile.DeduplicateStreamerBotEvents;
            SevenTvEmotesEnabled.IsChecked = profile.UseSevenTvEmotes;
            BetterTtvEmotesEnabled.IsChecked = profile.UseBetterTtvEmotes;
            FrankerFaceZEmotesEnabled.IsChecked = profile.UseFrankerFaceZEmotes;
            TwitchEnabled.IsChecked = profile.ShowTwitch;
            UseTwitchAccountAsChannelCheckBox.IsChecked = profile.UseTwitchAccountAsChannel;
            TwitchChannelBox.Text = profile.UseTwitchAccountAsChannel && !string.IsNullOrWhiteSpace(profile.TwitchUsername)
                ? profile.TwitchUsername
                : profile.TwitchChannel;
            TwitchChannelBox.IsEnabled = !profile.UseTwitchAccountAsChannel;
            YouTubeTargetBox.Text = profile.YouTubeTarget;
            FacebookLiveVideoIdBox.Text = profile.FacebookLiveVideoId;
            FacebookPollIntervalBox.Text = profile.FacebookPollIntervalSeconds.ToString();
            FacebookMaxPollIntervalBox.Text = profile.FacebookMaxPollIntervalSeconds.ToString();
            YouTubeEnabled.IsChecked = profile.ShowYouTube;
            KickEnabled.IsChecked = profile.ShowKick;
            FacebookEnabled.IsChecked = profile.ShowFacebook;
            StreamerBotEnabled.IsChecked = profile.ShowStreamerBot;
            StreamerBotUrlBox.Text = profile.StreamerBotUrl;
            CssPathBox.Text = profile.CustomCssPath ?? string.Empty;
            CustomCssBox.Text = profile.CustomCss;
            EnableAutoHideCheckBox.IsChecked = profile.EnableAutoHide;
            AutoHideSecondsBox.Text = profile.AutoHideSeconds.ToString();
            AutoHideOpacitySlider.Value = Math.Clamp(profile.AutoHideOpacity, 0.05, 0.5);
            EnableMessageFilteringCheckBox.IsChecked = profile.EnableMessageFiltering;
            AllowPatternsBox.Text = string.Join(Environment.NewLine, profile.MessageAllowPatterns);
            BlockPatternsBox.Text = string.Join(Environment.NewLine, profile.MessageBlockPatterns);
            IgnoredUsersBox.Text = string.Join(Environment.NewLine, profile.IgnoredUsers);

            // Update token references
            Settings.TwitchOAuthTokenProtected = profile.TwitchOAuthTokenProtected;
            Settings.TwitchRefreshTokenProtected = profile.TwitchRefreshTokenProtected;
            Settings.TwitchUsername = profile.TwitchUsername;
            Settings.KickClientSecretProtected = profile.KickClientSecretProtected;
            Settings.KickOAuthTokenProtected = profile.KickOAuthTokenProtected;
            Settings.KickRefreshTokenProtected = profile.KickRefreshTokenProtected;
            Settings.FacebookAccessTokenProtected = profile.FacebookAccessTokenProtected;

            SetTwitchAccountSignedIn(!string.IsNullOrWhiteSpace(profile.TwitchOAuthTokenProtected));
            KickAccountStatus.Text = string.IsNullOrWhiteSpace(profile.KickOAuthTokenProtected)
                ? "Not signed in"
                : "Signed in";
            FacebookAccountStatus.Text = string.IsNullOrWhiteSpace(profile.FacebookAccessTokenProtected)
                ? "Not signed in"
                : "Signed in";

            if (!string.IsNullOrWhiteSpace(profile.TwitchOAuthTokenProtected))
                _ = LoadTwitchAvatarAsync();

            foreach (var path in profile.CssThemePaths.Where(File.Exists))
                AddCssThemeOption(path);
            if (!string.IsNullOrWhiteSpace(profile.CustomCssPath) && File.Exists(profile.CustomCssPath))
            {
                var selectedTheme = FindCssThemeOption(profile.CustomCssPath) ?? AddCssThemeOption(profile.CustomCssPath);
                ThemePicker.SelectedItem = selectedTheme;
            }

            UpdateProfileInfo();
        }
        finally
        {
            _isLoadingProfiles = false;
        }
    }

    private void UpdateProfileInfo()
    {
        var active = _profileManager.GetActiveProfile();
        if (active != null)
        {
            var count = _profileManager.GetAllProfiles().Count;
            ProfileInfoText.Text = $"Profile: {active.Name}  |  Total profiles: {count}";
        }
        else
        {
            ProfileInfoText.Text = "No active profile";
        }
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TextInputDialog("New Profile", "Enter a name for the new profile:", "New Profile");
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ResponseText))
        {
            var newProfile = _profileManager.CreateProfile(dialog.ResponseText.Trim());
            LoadProfiles();
            ProfileSelector.SelectedItem = ProfileSelector.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => (i.Tag as ProfileSettings)?.Name == newProfile.Name);
        }
    }

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileSelector.SelectedItem is not ComboBoxItem selectedItem ||
            selectedItem.Tag is not ProfileSettings profile)
            return;

        var dialog = new TextInputDialog("Rename Profile", "Enter a new name:", profile.Name);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ResponseText))
        {
            var newName = dialog.ResponseText.Trim();
            // Check if name already exists
            if (_profileManager.GetAllProfiles().Any(p => p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            {
                System.Windows.MessageBox.Show("A profile with that name already exists.", "Duplicate name",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _profileManager.DeleteProfile(profile.Name);
            profile.Name = newName;
            _profileManager.SaveProfile(profile);
            LoadProfiles();
            ProfileSelector.SelectedItem = ProfileSelector.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => (i.Tag as ProfileSettings)?.Name == newName);
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileSelector.SelectedItem is not ComboBoxItem selectedItem ||
            selectedItem.Tag is not ProfileSettings profile)
            return;

        if (profile.Name == "Default")
        {
            System.Windows.MessageBox.Show("Cannot delete the Default profile.", "Delete profile",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = System.Windows.MessageBox.Show(
            $"Delete profile '{profile.Name}'? This cannot be undone.",
            "Delete profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            _profileManager.DeleteProfile(profile.Name);
            LoadProfiles();
            // Select the now-active profile
            var active = _profileManager.GetActiveProfile();
            if (active != null)
            {
                ProfileSelector.SelectedItem = ProfileSelector.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(i => (i.Tag as ProfileSettings)?.Name == active.Name);
            }
        }
    }

    private void DuplicateProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileSelector.SelectedItem is not ComboBoxItem selectedItem ||
            selectedItem.Tag is not ProfileSettings profile)
            return;

        var dialog = new TextInputDialog("Duplicate Profile", "Enter a name for the copy:", $"{profile.Name} (copy)");
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ResponseText))
        {
            var newProfile = _profileManager.CreateProfile(dialog.ResponseText.Trim(), profile);
            LoadProfiles();
            ProfileSelector.SelectedItem = ProfileSelector.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => (i.Tag as ProfileSettings)?.Name == newProfile.Name);
        }
    }

    private void SettingsNavigation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingsTabs is not null && SettingsNavigation.SelectedIndex >= 0)
            SettingsTabs.SelectedIndex = SettingsNavigation.SelectedIndex;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (TryApplySettings())
            DialogResult = true;
    }

    private bool TryApplySettings()
    {
        var twitchChannel = UseTwitchAccountAsChannelCheckBox.IsChecked == true &&
                            !string.IsNullOrWhiteSpace(Settings.TwitchUsername)
            ? Settings.TwitchUsername
            : TwitchChannelBox.Text.Trim().TrimStart('#');
        if (TwitchEnabled.IsChecked == true && twitchChannel.Length > 0 &&
            (twitchChannel.Length > 25 || twitchChannel.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')))
        {
            System.Windows.MessageBox.Show("Enter a valid Twitch channel login.", "Invalid channel", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (TwitchEnabled.IsChecked == true && UseTwitchAccountAsChannelCheckBox.IsChecked == true &&
            string.IsNullOrWhiteSpace(Settings.TwitchUsername))
        {
            System.Windows.MessageBox.Show("Sign in to Twitch to detect your account and use it as the channel.",
                "Twitch account required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (StreamerBotEnabled.IsChecked == true &&
            (!Uri.TryCreate(StreamerBotUrlBox.Text.Trim(), UriKind.Absolute, out var url) || url.Scheme is not ("ws" or "wss")))
        {
            System.Windows.MessageBox.Show("Enter a valid ws:// or wss:// Streamer.bot URL.", "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(CssPathBox.Text) && !File.Exists(CssPathBox.Text.Trim()))
        {
            System.Windows.MessageBox.Show("The selected CSS file does not exist.", "Invalid CSS file",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        Settings.Opacity = OpacitySlider.Value;
        Settings.DeduplicateMessages = DedupeCheckBox.IsChecked == true;
        Settings.DeduplicateStreamerBotMessages = StreamerBotMessageDedupe.IsChecked == true;
        Settings.DeduplicateStreamerBotEvents = StreamerBotEventDedupe.IsChecked == true;
        Settings.UseSevenTvEmotes = SevenTvEmotesEnabled.IsChecked == true;
        Settings.UseBetterTtvEmotes = BetterTtvEmotesEnabled.IsChecked == true;
        Settings.UseFrankerFaceZEmotes = FrankerFaceZEmotesEnabled.IsChecked == true;
        Settings.UseTwitchAccountAsChannel = UseTwitchAccountAsChannelCheckBox.IsChecked == true;
        Settings.ShowTwitch = TwitchEnabled.IsChecked == true;
        Settings.TwitchChannel = twitchChannel;
        Settings.ShowYouTube = YouTubeEnabled.IsChecked == true;
        Settings.YouTubeTarget = YouTubeTargetBox.Text.Trim();
        Settings.ShowKick = KickEnabled.IsChecked == true;
        if (ClearKickSecretCheckBox.IsChecked == true)
            Settings.KickClientSecretProtected = null;
        else if (!string.IsNullOrWhiteSpace(KickClientSecretBox.Password))
            Settings.KickClientSecretProtected = SettingsStore.ProtectToken(KickClientSecretBox.Password);
        Settings.ShowFacebook = FacebookEnabled.IsChecked == true;
        Settings.FacebookLiveVideoId = FacebookLiveVideoIdBox.Text.Trim();
        Settings.FacebookPollIntervalSeconds = int.TryParse(FacebookPollIntervalBox.Text, out var pollInterval) ? Math.Clamp(pollInterval, 1, 300) : 5;
        Settings.FacebookMaxPollIntervalSeconds = int.TryParse(FacebookMaxPollIntervalBox.Text, out var maxPollInterval) ? Math.Clamp(maxPollInterval, 5, 3600) : 60;
        Settings.ShowStreamerBot = StreamerBotEnabled.IsChecked == true;
        Settings.StreamerBotUrl = StreamerBotUrlBox.Text.Trim();
        Settings.CustomCssPath = string.IsNullOrWhiteSpace(CssPathBox.Text) ? null : CssPathBox.Text.Trim();
        Settings.CustomCss = CustomCssBox.Text;
        Settings.CssThemePaths = [.. Settings.CssThemePaths.Distinct(StringComparer.OrdinalIgnoreCase)];
        Settings.EnableAutoHide = EnableAutoHideCheckBox.IsChecked == true;
        Settings.AutoHideSeconds = int.TryParse(AutoHideSecondsBox.Text, out var autoHideSec) ? Math.Clamp(autoHideSec, 5, 3600) : 30;
        Settings.AutoHideOpacity = Math.Clamp(AutoHideOpacitySlider.Value, 0.05, 0.5);
        Settings.EnableMessageFiltering = EnableMessageFilteringCheckBox.IsChecked == true;
        Settings.MessageAllowPatterns = AllowPatternsBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        Settings.MessageBlockPatterns = BlockPatternsBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        Settings.IgnoredUsers = IgnoredUsersBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return true;
    }

    private async void TwitchSignIn_Click(object sender, RoutedEventArgs e)
    {
        TwitchSignInButton.IsEnabled = false;
        try
        {
            var (tokens, login) = await OAuthLoginService.SignInTwitchAsync(
                _twitchClientId,
                (uri, code) => System.Windows.MessageBox.Show(
                    this,
                    $"A browser opened to {uri}.\n\nEnter this one-time code to authorize chat and channel point redemption access:\n\n{code}",
                    "Twitch device authorization",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information));
            Settings.TwitchOAuthTokenProtected = SettingsStore.ProtectToken(tokens.AccessToken);
            if (!string.IsNullOrWhiteSpace(tokens.RefreshToken))
                Settings.TwitchRefreshTokenProtected = SettingsStore.ProtectToken(tokens.RefreshToken);
            Settings.TwitchUsername = login;
            if (UseTwitchAccountAsChannelCheckBox.IsChecked == true || string.IsNullOrWhiteSpace(TwitchChannelBox.Text))
                TwitchChannelBox.Text = login;
            TwitchChannelBox.IsEnabled = UseTwitchAccountAsChannelCheckBox.IsChecked != true;
            TwitchEnabled.IsChecked = true;
            SetTwitchAccountSignedIn(true, $"Signed in as {login}");
            await LoadTwitchAvatarAsync(tokens.AccessToken);
        }
        catch (OperationCanceledException)
        {
            TwitchAccountStatus.Text = "Sign-in cancelled";
        }
        catch (Exception ex)
        {
            ShowSignInError("Twitch", ex);
        }
        finally
        {
            TwitchSignInButton.IsEnabled = true;
        }
    }

    private async void TwitchLogOff_Click(object sender, RoutedEventArgs e)
    {
        TwitchLogOffButton.IsEnabled = false;
        var savedToken = Settings.TwitchOAuthTokenProtected;
        Settings.TwitchOAuthTokenProtected = null;
        Settings.TwitchRefreshTokenProtected = null;
        Settings.TwitchUsername = string.Empty;
        TwitchEnabled.IsChecked = false;
        TwitchAvatarImage.Source = null;
        SetTwitchAccountSignedIn(false);
        _twitchAvatarRequestId++;
        if (!TryApplySettings())
        {
            TwitchLogOffButton.IsEnabled = true;
            return;
        }

        string? revocationError = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(savedToken))
            {
                var accessToken = SettingsStore.UnprotectToken(savedToken);
                await OAuthLoginService.RevokeTwitchTokenAsync(_twitchClientId, accessToken);
            }
        }
        catch (Exception ex)
        {
            revocationError = ex.Message;
        }
        finally
        {
            TwitchLogOffButton.IsEnabled = true;
        }

        if (revocationError is not null)
        {
            System.Windows.MessageBox.Show(
                this,
                $"The saved Twitch sign-in will be removed from this app, but Twitch could not confirm token revocation: {revocationError}\n\nYou can revoke app access from your Twitch Connections settings.",
                "Twitch sign-out",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        DialogResult = true;
    }

    private void SetTwitchAccountSignedIn(bool isSignedIn, string? status = null)
    {
        TwitchAccountStatus.Text = status ??
            (isSignedIn ? $"Signed in as {Settings.TwitchUsername}" : "Not signed in");
        TwitchAccountStatus.ToolTip = null;
        TwitchAvatarImage.ToolTip = "Twitch profile avatar";
        TwitchSignInButton.Visibility = isSignedIn ? Visibility.Collapsed : Visibility.Visible;
        TwitchLogOffButton.Visibility = isSignedIn ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadTwitchAvatarAsync(string? accessToken = null)
    {
        var requestId = ++_twitchAvatarRequestId;
        try
        {
            accessToken ??= SettingsStore.UnprotectToken(Settings.TwitchOAuthTokenProtected
                ?? throw new InvalidOperationException("No Twitch sign-in is available."));
            var avatarBytes = await OAuthLoginService.GetTwitchAvatarAsync(
                _twitchClientId, Settings.TwitchUsername, accessToken);
            if (requestId != _twitchAvatarRequestId || avatarBytes is null)
                return;

            using var stream = new MemoryStream(avatarBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            TwitchAvatarImage.Source = bitmap;
        }
        catch (Exception ex)
        {
            if (requestId == _twitchAvatarRequestId)
                TwitchAvatarImage.ToolTip = $"Twitch avatar unavailable: {ex.Message}";
        }
    }

    private async void KickSignIn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var clientSecret = KickClientSecretBox.Password;
            if (string.IsNullOrWhiteSpace(clientSecret) &&
                !string.IsNullOrWhiteSpace(Settings.KickClientSecretProtected))
                clientSecret = SettingsStore.UnprotectToken(Settings.KickClientSecretProtected);

            var tokens = await OAuthLoginService.SignInKickAsync(
                _kickClientId,
                clientSecret);
            Settings.KickOAuthTokenProtected = SettingsStore.ProtectToken(tokens.AccessToken);
            if (!string.IsNullOrWhiteSpace(tokens.RefreshToken))
                Settings.KickRefreshTokenProtected = SettingsStore.ProtectToken(tokens.RefreshToken);
            if (!string.IsNullOrWhiteSpace(clientSecret))
                Settings.KickClientSecretProtected = SettingsStore.ProtectToken(clientSecret);
            KickAccountStatus.Text = "Signed in";
        }
        catch (Exception ex)
        {
            ShowSignInError("Kick", ex);
        }
    }

    private async void FacebookSignIn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var tokens = await OAuthLoginService.SignInFacebookAsync(_facebookAppId);
            Settings.FacebookAccessTokenProtected = SettingsStore.ProtectToken(tokens.AccessToken);
            FacebookAccountStatus.Text = "Signed in";
        }
        catch (OperationCanceledException)
        {
            FacebookAccountStatus.Text = "Sign-in cancelled";
        }
        catch (Exception ex)
        {
            ShowSignInError("Facebook", ex);
        }
    }

    private static void ShowSignInError(string provider, Exception exception) =>
        System.Windows.MessageBox.Show(
            $"{provider} sign-in failed: {exception.Message}",
            $"{provider} sign-in",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

    private void UseTwitchAccountAsChannel_Changed(object sender, RoutedEventArgs e)
    {
        var useAccount = UseTwitchAccountAsChannelCheckBox.IsChecked == true;
        TwitchChannelBox.IsEnabled = !useAccount;
        if (useAccount && !string.IsNullOrWhiteSpace(Settings.TwitchUsername))
            TwitchChannelBox.Text = Settings.TwitchUsername;
    }

    private async void BrowseCss_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose custom CSS",
            Filter = "CSS files (*.css)|*.css|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                var css = await File.ReadAllTextAsync(dialog.FileName);
                CssPathBox.Text = dialog.FileName;
                SetCssEditor(css, preserveFilePath: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Windows.MessageBox.Show(
                    this,
                    $"Could not read the selected CSS file: {ex.Message}",
                    "CSS file unavailable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private void CustomCssBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_isLoadingCss && !string.IsNullOrWhiteSpace(CssPathBox.Text))
            CssPathBox.Clear();

        CssPreviewChanged?.Invoke(CustomCssBox.Text);
        _ = UpdateCssPreviewAsync(CustomCssBox.Text);
    }

    private void ResetCss_Click(object sender, RoutedEventArgs e)
    {
        CssPathBox.Clear();
        CustomCssBox.Clear();
        ThemePicker.SelectedIndex = 0;
    }

    private async void AddCssTheme_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add a CSS theme",
            Filter = "CSS files (*.css)|*.css|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var path = Path.GetFullPath(dialog.FileName);
        try
        {
            var css = await File.ReadAllTextAsync(path);
            if (!Settings.CssThemePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                Settings.CssThemePaths.Add(path);

            var option = FindCssThemeOption(path) ?? AddCssThemeOption(path);
            ThemePicker.SelectedItem = option;
            CssPathBox.Text = path;
            SetCssEditor(css, preserveFilePath: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Could not add the selected CSS theme: {ex.Message}",
                "CSS theme unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ApplyTheme_Click(object sender, RoutedEventArgs e)
    {
        if (ThemePicker.SelectedItem is not ComboBoxItem selectedTheme)
            return;

        if (selectedTheme.Tag is string path)
        {
            try
            {
                var themeFileCss = await File.ReadAllTextAsync(path);
                CssPathBox.Text = path;
                SetCssEditor(themeFileCss, preserveFilePath: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Windows.MessageBox.Show(
                    this,
                    $"Could not load the selected CSS theme: {ex.Message}",
                    "CSS theme unavailable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return;
        }

        var theme = selectedTheme.Content?.ToString();
        var css = theme switch
        {
            "Compact" => ":root { --chat-size: 13px; --message-radius: 2px; --line-height: 1.15; }\n.message, .event { padding: 2px 5px; }\n#chat { gap: 2px; padding: 4px; }",
            "High contrast" => ":root { --message-bg: rgba(0, 0, 0, .88); --text-shadow: 0 1px 2px #000; }\n.message, .event { outline: 1px solid rgba(255,255,255,.55); }\n.username { color: #fff !important; }",
            "Sleek Dark" => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "sleek-dark.css")),
            _ => string.Empty
        };
        CssPathBox.Clear();
        SetCssEditor(css, preserveFilePath: false);
    }

    private void SetCssEditor(string css, bool preserveFilePath)
    {
        _isLoadingCss = true;
        CustomCssBox.Text = css;
        _isLoadingCss = false;
        if (!preserveFilePath)
            CssPathBox.Clear();

        CssPreviewChanged?.Invoke(css);
        _ = UpdateCssPreviewAsync(css);
    }

    private ComboBoxItem AddCssThemeOption(string path)
    {
        var option = new ComboBoxItem
        {
            Content = Path.GetFileNameWithoutExtension(path),
            Tag = path,
            ToolTip = path
        };
        ThemePicker.Items.Add(option);
        return option;
    }

    private ComboBoxItem? FindCssThemeOption(string path) =>
        ThemePicker.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is string themePath &&
                                    string.Equals(themePath, path, StringComparison.OrdinalIgnoreCase));

    private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            CssPreviewView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 20, 20, 20);
            await CssPreviewView.EnsureCoreWebView2Async();
            CssPreviewView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            CssPreviewView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            CssPreviewView.CoreWebView2.NavigationCompleted += CssPreview_NavigationCompleted;
            CssPreviewView.Source = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "chat.html"));
        }
        catch (Exception ex)
        {
            CssPreviewStatus.Text = $"Preview unavailable: {ex.Message}";
        }
    }

    private async void CssPreview_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            CssPreviewStatus.Text = $"Preview page failed to load: {e.WebErrorStatus}";
            return;
        }

        _cssPreviewReady = true;
        var css = CustomCssBox.Text;
        if (string.IsNullOrWhiteSpace(css) && !string.IsNullOrWhiteSpace(CssPathBox.Text))
        {
            try
            {
                css = await File.ReadAllTextAsync(CssPathBox.Text);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CssPreviewStatus.Text = $"Could not load the selected CSS file: {ex.Message}";
                return;
            }
        }

        await UpdateCssPreviewAsync(css);
    }

    private async Task UpdateCssPreviewAsync(string css)
    {
        if (!_cssPreviewReady || CssPreviewView.CoreWebView2 is null)
            return;

        try
        {
            var encodedCss = JsonSerializer.Serialize(css);
            await CssPreviewView.CoreWebView2.ExecuteScriptAsync($"window.overlay?.setCustomCss({encodedCss});");
            if (!_cssPreviewSamplesPublished)
            {
                PublishCssPreviewSamples();
                _cssPreviewSamplesPublished = true;
            }

            CssPreviewStatus.Text = "Preview shows sample chat messages and an event.";
        }
        catch (Exception ex)
        {
            CssPreviewStatus.Text = $"Preview update failed: {ex.Message}";
        }
    }

    private void PublishCssPreviewSamples()
    {
        var now = DateTimeOffset.UtcNow;
        var messages = new[]
        {
            new ChatMessage
            {
                Id = "preview-twitch",
                Platform = ChatPlatform.Twitch,
                Username = "chat_viewer",
                DisplayName = "ChatViewer",
                UserColor = "#c084fc",
                IsSubscriber = true,
                MessageParts = [new TextPart("This is a sample chat message.")]
            },
            new ChatMessage
            {
                Id = "preview-youtube",
                Platform = ChatPlatform.YouTube,
                Username = "youtube_viewer",
                DisplayName = "YouTubeViewer",
                UserColor = "#60a5fa",
                MessageParts = [new TextPart("Preview your name, badges, spacing, and message colors.")]
            }
        };

        foreach (var message in messages)
            CssPreviewView.CoreWebView2.PostWebMessageAsJson(
                JsonSerializer.Serialize(new { message, @event = (ChatEvent?)null }, PreviewJsonOptions));

        var chatEvent = new ChatEvent
        {
            Id = "preview-event",
            Type = "superchat",
            Platform = ChatPlatform.YouTube,
            User = "Supporter",
            Amount = "$5.00",
            Message = "Sample event alert",
            Timestamp = now
        };
        CssPreviewView.CoreWebView2.PostWebMessageAsJson(
            JsonSerializer.Serialize(new { message = (ChatMessage?)null, @event = chatEvent }, PreviewJsonOptions));
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e) => CssPreviewView.Dispose();

    private void ExportPreview_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var html = GenerateExportHtml();
            var tempPath = Path.Combine(Path.GetTempPath(), "MultiChatOverlay_ExportPreview.html");
            File.WriteAllText(tempPath, html);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true
            });
            ExportStatusText.Text = "Preview opened in browser";
        }
        catch (Exception ex)
        {
            ExportStatusText.Text = $"Preview failed: {ex.Message}";
        }
    }

    private void ExportSave_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save OBS Browser Source HTML",
            Filter = "HTML files (*.html)|*.html|All files (*.*)|*.*",
            DefaultExt = "html",
            FileName = "MultiChatOverlay_Export.html"
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                var html = GenerateExportHtml();
                File.WriteAllText(dialog.FileName, html);
                ExportStatusText.Text = $"Exported to {dialog.FileName}";
            }
            catch (Exception ex)
            {
                ExportStatusText.Text = $"Export failed: {ex.Message}";
            }
        }
    }

    private string GenerateExportHtml()
    {
        // Get theme CSS
        string themeCss = string.Empty;
        var selectedTheme = ExportThemePicker.SelectedItem as ComboBoxItem;
        var themeName = selectedTheme?.Content?.ToString() ?? "Current Theme";

        if (themeName == "Current Theme")
        {
            themeCss = CustomCssBox.Text;
        }
        else
        {
            themeCss = themeName switch
            {
                "Compact" => ":root { --chat-size: 13px; --message-radius: 2px; --line-height: 1.15; }\n.message, .event { padding: 2px 5px; }\n#chat { gap: 2px; padding: 4px; }",
                "High contrast" => ":root { --message-bg: rgba(0, 0, 0, .88); --text-shadow: 0 1px 2px #000; }\n.message, .event { outline: 1px solid rgba(255,255,255,.55); }\n.username { color: #fff !important; }",
                "Sleek Dark" => File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "sleek-dark.css"))
                    ? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "sleek-dark.css"))
                    : string.Empty,
                _ => string.Empty
            };
        }

        // Get inline CSS
        var inlineCss = ExportIncludeCustomCssCheckBox.IsChecked == true ? CustomCssBox.Text : string.Empty;

        // Background
        string backgroundStyle = ExportBackgroundPicker.SelectedItem is ComboBoxItem bgItem
            ? bgItem.Content?.ToString() ?? "Transparent"
            : "Transparent";

        string backgroundCss = backgroundStyle switch
        {
            "Transparent" => "background: transparent;",
            "Semi-transparent dark (#1B1B1BCC)" => "background: #1B1B1BCC;",
            "Opaque dark (#1B1B1B)" => "background: #1B1B1B;",
            _ => "background: transparent;"
        };

        // Read chat.js
        string chatJs = string.Empty;
        var chatJsPath = Path.Combine(AppContext.BaseDirectory, "Assets", "chat.js");
        if (File.Exists(chatJsPath))
        {
            chatJs = File.ReadAllText(chatJsPath);
        }

        // Read chat.html template
        string chatHtml = string.Empty;
        var chatHtmlPath = Path.Combine(AppContext.BaseDirectory, "Assets", "chat.html");
        if (File.Exists(chatHtmlPath))
        {
            chatHtml = File.ReadAllText(chatHtmlPath);
        }

        // Build the exported HTML
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("    <meta charset=\"UTF-8\">");
        sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine("    <title>MultiChatOverlay Export</title>");
        sb.AppendLine("    <style>");
        sb.AppendLine("        * { box-sizing: border-box; }");
        sb.AppendLine($"        html, body {{ margin: 0; padding: 0; height: 100%; overflow: hidden; {backgroundCss} font-family: system-ui, -apple-system, sans-serif; }}");
        sb.AppendLine("        #chat { display: flex; flex-direction: column-reverse; height: 100%; overflow-y: auto; padding: 8px; gap: 4px; }");
        sb.AppendLine("        .message, .event { display: flex; flex-direction: column; max-width: 100%; animation: fadeIn 0.3s ease-out; }");
        sb.AppendLine("        .message-header { display: flex; align-items: baseline; gap: 6px; flex-wrap: wrap; }");
        sb.AppendLine("        .badge { display: inline-flex; align-items: center; height: 16px; padding: 0 4px; font-size: 10px; border-radius: 2px; background: rgba(255,255,255,0.1); }");
        sb.AppendLine("        .username { font-weight: 600; font-size: 13px; }");
        sb.AppendLine("        .message-text { margin-left: 20px; font-size: 14px; line-height: 1.4; word-wrap: break-word; }");
        sb.AppendLine("        .emote { display: inline-block; height: 1.2em; vertical-align: middle; margin: 0 1px; }");
        sb.AppendLine("        .event { border-left: 3px solid #fbbf24; padding: 6px 10px; background: rgba(251,191,36,0.15); border-radius: 4px; }");
        sb.AppendLine("        .event-header { display: flex; align-items: center; gap: 6px; font-weight: 600; font-size: 12px; color: #fbbf24; }");
        sb.AppendLine("        .event-text { margin-top: 4px; font-size: 13px; }");
        sb.AppendLine("        .timestamp { font-size: 11px; opacity: 0.6; margin-left: auto; }");
        sb.AppendLine("        @keyframes fadeIn { from { opacity: 0; transform: translateY(10px); } to { opacity: 1; transform: translateY(0); } }");
        sb.AppendLine("        .sample-badge { background: linear-gradient(90deg, #c084fc, #a855f7); color: white; }");
        sb.AppendLine("        .subscriber-badge { background: linear-gradient(90deg, #60a5fa, #3b82f6); color: white; }");
        sb.AppendLine("        .moderator-badge { background: linear-gradient(90deg, #34d399, #10b981); color: white; }");

        // Add theme CSS
        if (!string.IsNullOrWhiteSpace(themeCss))
        {
            sb.AppendLine("        /* Theme CSS */");
            foreach (var line in themeCss.Split('\n'))
                sb.AppendLine("        " + line);
        }

        // Add inline CSS
        if (!string.IsNullOrWhiteSpace(inlineCss))
        {
            sb.AppendLine("        /* Inline CSS Override */");
            foreach (var line in inlineCss.Split('\n'))
                sb.AppendLine("        " + line);
        }

        sb.AppendLine("    </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("    <div id=\"chat\"></div>");

        // Include chat.js
        sb.AppendLine("    <script>");
        if (!string.IsNullOrWhiteSpace(chatJs))
        {
            foreach (var line in chatJs.Split('\n'))
                sb.AppendLine("        " + line);
        }
        else
        {
            // Minimal fallback
            sb.AppendLine("        (function() {");
            sb.AppendLine("            const chat = document.getElementById('chat');");
            sb.AppendLine("            window.overlay = {");
            sb.AppendLine("                addMessage: function(msg) {");
            sb.AppendLine("                    const div = document.createElement('div');");
            sb.AppendLine("                    div.className = 'message';");
            sb.AppendLine("                    div.innerHTML = `<div class='message-header'><span class='username'>${msg.displayName}</span><span class='timestamp'>${new Date(msg.timestamp).toLocaleTimeString()}</span></div><div class='message-text'>${msg.message}</div>`;");
            sb.AppendLine("                    chat.insertBefore(div, chat.firstChild);");
            sb.AppendLine("                },");
            sb.AppendLine("                addEvent: function(evt) {");
            sb.AppendLine("                    const div = document.createElement('div');");
            sb.AppendLine("                    div.className = 'event';");
            sb.AppendLine("                    div.innerHTML = `<div class='event-header'>${evt.type}: ${evt.user}</div><div class='event-text'>${evt.message || ''}</div>`;");
            sb.AppendLine("                    chat.insertBefore(div, chat.firstChild);");
            sb.AppendLine("                }");
            sb.AppendLine("            };");
            sb.AppendLine("        })();");
        }
        sb.AppendLine("    </script>");

        // Add sample messages if enabled
        if (ExportIncludeSampleMessagesCheckBox.IsChecked == true)
        {
            sb.AppendLine("    <script>");
            sb.AppendLine("        window.addEventListener('DOMContentLoaded', function() {");
            sb.AppendLine("            setTimeout(function() {");
            sb.AppendLine("                if (window.overlay) {");
            sb.AppendLine("                    window.overlay.addMessage({");
            sb.AppendLine("                        platform: 'twitch',");
            sb.AppendLine("                        username: 'chat_viewer',");
            sb.AppendLine("                        displayName: 'ChatViewer',");
            sb.AppendLine("                        userColor: '#c084fc',");
            sb.AppendLine("                        message: 'This is a sample chat message.',");
            sb.AppendLine("                        timestamp: new Date().toISOString(),");
            sb.AppendLine("                        badges: [{name: 'subscriber', version: '1'}]");
            sb.AppendLine("                    });");
            sb.AppendLine("                    window.overlay.addMessage({");
            sb.AppendLine("                        platform: 'youtube',");
            sb.AppendLine("                        username: 'youtube_viewer',");
            sb.AppendLine("                        displayName: 'YouTubeViewer',");
            sb.AppendLine("                        userColor: '#60a5fa',");
            sb.AppendLine("                        message: 'Preview your name, badges, spacing, and message colors.',");
            sb.AppendLine("                        timestamp: new Date().toISOString()");
            sb.AppendLine("                    });");
            sb.AppendLine("                    window.overlay.addEvent({");
            sb.AppendLine("                        type: 'superchat',");
            sb.AppendLine("                        platform: 'youtube',");
            sb.AppendLine("                        user: 'Supporter',");
            sb.AppendLine("                        amount: '$5.00',");
            sb.AppendLine("                        message: 'Sample event alert',");
            sb.AppendLine("                        timestamp: new Date().toISOString()");
            sb.AppendLine("                    });");
            sb.AppendLine("                }");
            sb.AppendLine("            }, 100);");
            sb.AppendLine("        });");
            sb.AppendLine("    </script>");
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

}
