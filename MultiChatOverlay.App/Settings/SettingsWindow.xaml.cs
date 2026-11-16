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

    public SettingsWindow(OverlaySettings settings)
    {
        InitializeComponent();
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
            CssThemePaths = [.. settings.CssThemePaths]
        };
        OpacitySlider.Value = Settings.Opacity;
        DedupeCheckBox.IsChecked = Settings.DeduplicateMessages;
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
        FacebookAccountStatus.Text = string.IsNullOrWhiteSpace(Settings.FacebookAccessTokenProtected)
            ? "Not signed in"
            : "Signed in";
        YouTubeEnabled.IsChecked = Settings.ShowYouTube;
        KickEnabled.IsChecked = Settings.ShowKick;
        FacebookEnabled.IsChecked = Settings.ShowFacebook;
        StreamerBotEnabled.IsChecked = Settings.ShowStreamerBot;
        StreamerBotUrlBox.Text = Settings.StreamerBotUrl;
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
        Settings.ShowStreamerBot = StreamerBotEnabled.IsChecked == true;
        Settings.StreamerBotUrl = StreamerBotUrlBox.Text.Trim();
        Settings.CustomCssPath = string.IsNullOrWhiteSpace(CssPathBox.Text) ? null : CssPathBox.Text.Trim();
        Settings.CustomCss = CustomCssBox.Text;
        Settings.CssThemePaths = [.. Settings.CssThemePaths.Distinct(StringComparer.OrdinalIgnoreCase)];
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
        if (dialog.ShowDialog(this) == true)
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

}
