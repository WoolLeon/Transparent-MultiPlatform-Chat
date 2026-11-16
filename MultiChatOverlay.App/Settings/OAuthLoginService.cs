using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace MultiChatOverlay.App.Settings;

public sealed record OAuthTokens(string AccessToken, string? RefreshToken = null);

public static class OAuthLoginService
{
    private static readonly HttpClient HttpClient = new();
    private const string KickRedirectUri = "http://localhost:43821/callback/";

    public static async Task<(OAuthTokens Tokens, string Login)> SignInTwitchAsync(
        string clientId,
        Action<string, string> showDeviceCode,
        CancellationToken cancellationToken = default)
    {
        RequireClientId(clientId, "Twitch");
        using var deviceResponse = await HttpClient.PostAsync(
            "https://id.twitch.tv/oauth2/device",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["scopes"] = "chat:read channel:read:redemptions"
            }),
            cancellationToken);
        deviceResponse.EnsureSuccessStatusCode();
        using var deviceDocument = await JsonDocument.ParseAsync(
            await deviceResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var device = deviceDocument.RootElement;
        var deviceCode = RequiredString(device, "device_code");
        var userCode = RequiredString(device, "user_code");
        var verificationUri = RequiredString(device, "verification_uri");
        var expiresIn = device.GetProperty("expires_in").GetInt32();
        var intervalSeconds = device.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 5;

        OpenBrowser(verificationUri);
        showDeviceCode(verificationUri, userCode);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(expiresIn));
        while (!timeout.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, intervalSeconds)), timeout.Token);
            using var tokenResponse = await HttpClient.PostAsync(
                "https://id.twitch.tv/oauth2/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["scopes"] = "chat:read channel:read:redemptions",
                    ["device_code"] = deviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                }),
                timeout.Token);
            var tokenBody = await tokenResponse.Content.ReadAsStringAsync(timeout.Token);
            using var tokenDocument = JsonDocument.Parse(tokenBody);
            if (tokenResponse.IsSuccessStatusCode)
            {
                var accessToken = RequiredString(tokenDocument.RootElement, "access_token");
                using var validateRequest = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
                validateRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("OAuth", accessToken);
                using var validateResponse = await HttpClient.SendAsync(validateRequest, timeout.Token);
                validateResponse.EnsureSuccessStatusCode();
                using var validateDocument = await JsonDocument.ParseAsync(
                    await validateResponse.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
                if (!string.Equals(RequiredString(validateDocument.RootElement, "client_id"), clientId, StringComparison.Ordinal))
                    throw new InvalidDataException("The Twitch access token belongs to a different OAuth client.");
                var refreshToken = tokenDocument.RootElement.TryGetProperty("refresh_token", out var refresh)
                    ? refresh.GetString()
                    : null;
                return (new OAuthTokens(accessToken, refreshToken), RequiredString(validateDocument.RootElement, "login"));
            }

            var error = tokenDocument.RootElement.TryGetProperty("message", out var message)
                ? message.GetString()
                : tokenDocument.RootElement.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;
            if (error is "authorization_pending" or "Authorization pending")
                continue;
            if (error is "slow_down" or "Slow down")
            {
                intervalSeconds += 5;
                continue;
            }

            throw new HttpRequestException($"Twitch device authorization failed: {error ?? tokenResponse.StatusCode.ToString()}");
        }

        throw new TimeoutException("Twitch sign-in expired before authorization completed.");
    }

    public static async Task<(OAuthTokens Tokens, string Login)> EnsureValidTwitchTokenAsync(
        string? clientId,
        string accessToken,
        string? refreshToken,
        CancellationToken cancellationToken = default)
    {
        var login = await ValidateTwitchTokenAsync(clientId, accessToken, cancellationToken);
        if (login is not null)
            return (new OAuthTokens(accessToken, refreshToken), login);

        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new UnauthorizedAccessException(
                "The saved Twitch access token has expired. Sign in to Twitch again in Settings.");
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException(
                "The Twitch access token has expired. Restore the Twitch OAuth client ID before signing in again.");

        using var refreshResponse = await HttpClient.PostAsync(
            "https://id.twitch.tv/oauth2/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            }),
            cancellationToken);
        var refreshBody = await refreshResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!refreshResponse.IsSuccessStatusCode)
        {
            if (refreshResponse.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException(
                    "Twitch rejected the saved refresh token. Sign in to Twitch again in Settings.");

            throw new HttpRequestException(
                $"Twitch token refresh failed with HTTP {(int)refreshResponse.StatusCode}.",
                null,
                refreshResponse.StatusCode);
        }

        using var tokenDocument = JsonDocument.Parse(refreshBody);
        var refreshedAccessToken = RequiredString(tokenDocument.RootElement, "access_token");
        var refreshedRefreshToken = tokenDocument.RootElement.TryGetProperty("refresh_token", out var refresh)
            ? refresh.GetString()
            : refreshToken;
        var refreshedLogin = await ValidateTwitchTokenAsync(clientId, refreshedAccessToken, cancellationToken)
            ?? throw new UnauthorizedAccessException(
                "Twitch rejected the refreshed access token. Sign in to Twitch again in Settings.");
        return (new OAuthTokens(refreshedAccessToken, refreshedRefreshToken), refreshedLogin);
    }

    public static async Task<byte[]?> GetTwitchAvatarAsync(
        string clientId,
        string login,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        RequireClientId(clientId, "Twitch");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.twitch.tv/helix/users?login={Uri.EscapeDataString(login)}");
        request.Headers.Add("Client-Id", clientId);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var users = document.RootElement.GetProperty("data");
        if (users.GetArrayLength() == 0 ||
            !users[0].TryGetProperty("profile_image_url", out var imageUrl) ||
            string.IsNullOrWhiteSpace(imageUrl.GetString()))
            return null;

        using var imageResponse = await HttpClient.GetAsync(imageUrl.GetString(), cancellationToken);
        imageResponse.EnsureSuccessStatusCode();
        return await imageResponse.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public static async Task RevokeTwitchTokenAsync(
        string clientId,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        RequireClientId(clientId, "Twitch");
        using var response = await HttpClient.PostAsync(
            "https://id.twitch.tv/oauth2/revoke",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["token"] = accessToken
            }),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string?> ValidateTwitchTokenAsync(
        string? clientId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var validateRequest = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
        validateRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("OAuth", accessToken);
        using var validateResponse = await HttpClient.SendAsync(validateRequest, cancellationToken);
        if (validateResponse.StatusCode == HttpStatusCode.Unauthorized)
            return null;

        validateResponse.EnsureSuccessStatusCode();
        using var validateDocument = await JsonDocument.ParseAsync(
            await validateResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var validatedClientId = RequiredString(validateDocument.RootElement, "client_id");
        if (!string.IsNullOrWhiteSpace(clientId) &&
            !string.Equals(validatedClientId, clientId, StringComparison.Ordinal))
            throw new InvalidDataException("The Twitch access token belongs to a different OAuth client.");
        return RequiredString(validateDocument.RootElement, "login");
    }

    public static async Task<OAuthTokens> SignInKickAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default)
    {
        RequireClientId(clientId, "Kick");
        if (string.IsNullOrWhiteSpace(clientSecret))
            throw new ArgumentException("Kick requires your Kick application's client secret for the token exchange.", nameof(clientSecret));

        var verifier = CreateVerifier();
        var challenge = CreateChallenge(verifier);
        var state = CreateVerifier();
        using var listener = StartListener(KickRedirectUri);
        var authorizeUri = BuildUri("https://id.kick.com/oauth/authorize", new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = KickRedirectUri,
            ["scope"] = "user:read events:subscribe",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state
        });
        OpenBrowser(authorizeUri);
        var callback = await ReceiveCallbackAsync(listener, cancellationToken);
        ValidateState(callback, state);
        var authorizationError = QueryValue(callback.Query, "error");
        if (!string.IsNullOrWhiteSpace(authorizationError))
        {
            var description = QueryValue(callback.Query, "error_description");
            throw new InvalidDataException(
                $"Kick authorization failed ({authorizationError})" +
                (string.IsNullOrWhiteSpace(description) ? "." : $": {description}"));
        }

        var code = RequiredQueryValue(callback, "code");
        using var tokenResponse = await HttpClient.PostAsync(
            "https://id.kick.com/oauth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = KickRedirectUri,
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = verifier
            }),
            cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenDocument = await JsonDocument.ParseAsync(
            await tokenResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return ParseTokens(tokenDocument.RootElement);
    }

    public static async Task<OAuthTokens> SignInFacebookAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        RequireClientId(appId, "Facebook");
        var state = CreateVerifier();
        var redirectUri = "https://www.facebook.com/connect/login_success.html";
        var authorizeUri = BuildUri("https://www.facebook.com/v27.0/dialog/oauth", new Dictionary<string, string>
        {
            ["client_id"] = appId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "token",
            ["scope"] = "pages_read_engagement,pages_read_user_content",
            ["state"] = state
        });
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        var loginWindow = new FacebookLoginWindow(authorizeUri, state);
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            var token = await loginWindow.ShowLoginAsync(cancellationToken);
            if (token is not null)
                completion.TrySetResult(token);
            else
                completion.TrySetCanceled(cancellationToken);
        }).Task.Unwrap();
        return new OAuthTokens(await completion.Task);
    }

    private static HttpListener StartListener(string redirectUri)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();
        return listener;
    }

    private static async Task<Uri> ReceiveCallbackAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        while (true)
        {
            var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
            var callback = context.Request.Url ?? throw new InvalidDataException("OAuth callback URL was empty.");
            var hasResult = callback.Query.Contains("code=", StringComparison.Ordinal) ||
                            callback.Query.Contains("error=", StringComparison.Ordinal);
            if (!hasResult)
            {
                // Browser probes such as /favicon.ico must not consume the one real callback.
                context.Response.StatusCode = 404;
                context.Response.Close();
                continue;
            }

            var page = callback.Query.Contains("error=", StringComparison.OrdinalIgnoreCase)
                ? "<html><body>Sign-in was not completed. You may close this window.</body></html>"
                : "<html><body>Sign-in complete. You may close this window and return to Multi Chat Overlay.</body></html>";
            var bytes = Encoding.UTF8.GetBytes(page);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, timeout.Token);
            context.Response.Close();
            return callback;
        }
    }

    private static void ValidateState(Uri callback, string expectedState)
    {
        var actualState = QueryValue(callback.Query, "state");
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(actualState ?? string.Empty),
                Encoding.UTF8.GetBytes(expectedState)))
            throw new InvalidDataException("OAuth state validation failed. Please retry sign-in.");
    }

    private static string RequiredQueryValue(Uri uri, string key)
    {
        var value = QueryValue(uri.Query, key);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"OAuth callback did not include '{key}'.");
        return value;
    }

    private static string? QueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var name = separator < 0 ? pair : pair[..separator];
            if (!string.Equals(Uri.UnescapeDataString(name), key, StringComparison.Ordinal))
                continue;
            return separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
        }

        return null;
    }

    private static OAuthTokens ParseTokens(JsonElement tokenObject) =>
        new(RequiredString(tokenObject, "access_token"),
            tokenObject.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null);

    private static string RequiredString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"OAuth response did not include '{property}'.");
        return value.GetString()!;
    }

    private static string BuildUri(string endpoint, IReadOnlyDictionary<string, string> parameters) =>
        endpoint + "?" + string.Join("&", parameters.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

    private static void OpenBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string CreateChallenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void RequireClientId(string value, string provider)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(
                $"Add the {provider} OAuth client ID to settings.json and restart the app before signing in.",
                nameof(value));
    }
}

public sealed class FacebookLoginWindow : System.Windows.Window
{
    private readonly string _authorizeUri;
    private readonly string _expectedState;
    private readonly TaskCompletionSource<string> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Microsoft.Web.WebView2.Wpf.WebView2 _webView = new();

    public FacebookLoginWindow(
        string authorizeUri,
        string expectedState)
    {
        _authorizeUri = authorizeUri;
        _expectedState = expectedState;
        Title = "Sign in to Facebook";
        Width = 520;
        Height = 680;
        WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
        Owner = System.Windows.Application.Current.Windows
            .OfType<System.Windows.Window>()
            .FirstOrDefault(window => window.IsActive);
        Content = _webView;
        _webView.NavigationStarting += NavigationStarting;
        Closed += (_, _) =>
        {
            _completion.TrySetCanceled();
            _webView.Dispose();
        };
    }

    public async Task<string?> ShowLoginAsync(CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() => Dispatcher.Invoke(Close));
        await _webView.EnsureCoreWebView2Async();
        _webView.Source = new Uri(_authorizeUri);
        ShowDialog();
        try
        {
            return await _completion.Task;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private void NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("www.facebook.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.Equals("/connect/login_success.html", StringComparison.OrdinalIgnoreCase))
            return;

        e.Cancel = true;
        var fragment = uri.Fragment.TrimStart('#');
        var fields = fragment.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]),
                pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty,
                StringComparer.Ordinal);
        if (!fields.TryGetValue("state", out var state) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(_expectedState)))
        {
            _completion.TrySetException(new InvalidDataException("Facebook OAuth state validation failed."));
            Close();
            return;
        }

        if (fields.TryGetValue("access_token", out var token) && !string.IsNullOrWhiteSpace(token))
            _completion.TrySetResult(token);
        else
            _completion.TrySetException(new InvalidDataException(fields.GetValueOrDefault("error_description") ?? "Facebook sign-in was not completed."));
        Close();
    }
}
