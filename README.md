# Multi Chat Overlay

## Architecture and verified API assumptions

This Windows desktop app is built as a .NET 8 WPF shell around a transparent WebView2 chat page. `MultiChatOverlay.Core` owns normalized chat models, source interfaces, Twitch parsing, connection state, deduplication, and source adapters. `MultiChatOverlay.App` owns the overlay window, tray/hotkeys, encrypted settings, logging, and WebView2 renderer. `MultiChatOverlay.Tests` covers Twitch IRC parsing and deduplication.

The implementation uses documented public APIs where available; YouTube chat is an explicit exception:

- Twitch chat uses IRC over `wss://irc-ws.chat.twitch.tv:443`; channel-point redemptions use Twitch EventSub over its WebSocket transport, independently of Streamer.bot. Sign-in uses Twitch's device-code flow and requests `chat:read` and `channel:read:redemptions`. The signed-in account must own the configured channel for redemption subscriptions. Existing Twitch users must sign in again to grant the redemption scope. See [Twitch OAuth docs](https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/), [Twitch IRC docs](https://dev.twitch.tv/docs/chat/irc/), and [Twitch EventSub docs](https://dev.twitch.tv/docs/eventsub/handling-websocket-events/).
- YouTube chat uses [Agash/YTLiveChat](https://github.com/Agash/YTLiveChat), an unofficial .NET client for YouTube's internal InnerTube chat interface. It needs no Google OAuth or YouTube Data API quota, and supports messages, Super Chats, Super Stickers, and membership events. Its parser can break if YouTube changes its undocumented payloads; respect its request-frequency guidance. Restricted chats may not be accessible.
- Facebook polls the documented `/{live-video-id}/comments` edge, requests stream comments newest-first, and uses an access token. The live-video comments edge and its filters are documented by Meta; required app/Page access varies with the Page and app review configuration. See [Meta live-video comments](https://developers.facebook.com/docs/graph-api/reference/live-video/comments/).
- Streamer.bot uses its documented JSON WebSocket request envelope, explicit `Subscribe`, `DoAction`, and `SendMessage` requests. Event names are subscribed per official event catalogs. See [Streamer.bot WebSocket requests](https://docs.streamer.bot/api/websocket/requests), [Twitch events](https://docs.streamer.bot/api/websocket/events/twitch), [YouTube events](https://docs.streamer.bot/api/websocket/events/youtube), and [Kick events](https://docs.streamer.bot/api/websocket/events/kick).
- Kick's current official developer API supports webhook event subscriptions, which require an externally reachable webhook endpoint. A local overlay does not expose a public webhook receiver; this build accepts Kick events relayed by Streamer.bot. It does not invent a Kick Pusher endpoint or depend on undocumented web-client internals. See [Kick event subscription docs](https://docs.kick.com/events/subscribe-to-events.md).

## Project structure

```text
MultiChatOverlay.sln
MultiChatOverlay.Core/
  Models/             Normalized chat messages, events, and message parts
  Parsing/            Twitch IRC tags and emote-range parser
  Services/           Cross-source message deduplicator
  Sources/            Twitch IRC and EventSub, YouTube InnerTube, Facebook
                      Graph API, Streamer.bot WebSocket, and IChatSource
MultiChatOverlay.App/
  Assets/             chat.html, chat.js, default.css
  Logging/            Serilog rolling-file logger
  Settings/           Settings model, DPAPI storage, OAuth .env configuration, settings dialog
  MainWindow.xaml     Transparent WPF overlay, tray, hotkeys, lock mode
MultiChatOverlay.Tests/
  TwitchIrcParserTests.cs
  MessageDeduplicatorTests.cs
installer/
  MultiChatOverlay.iss
publish.ps1
```

## Implemented in this build

- Borderless, topmost, transparent overlay with remembered location, dimensions, and opacity.
- Larger draggable black top bar with a menu button, per-platform connection indicators, a click-through toggle, and a hide button; the menu includes chat zoom, opacity, clear, settings, and bring-to-top actions.
- Slightly thicker overlay outline, hidden with the window controls in click-through mode.
- Freely resizable from the window edges/corners and through the bottom-right resize grip; lock mode hides the chrome and enables click-through/no-activate window styles.
- Global hotkeys and a notification-area menu.
- Twitch IRC (OAuth authenticated), YouTube InnerTube live chat, Facebook live-video comments polling, and Streamer.bot event subscriptions with exponential reconnect.
- Browser sign-in for Twitch (device code), Facebook (embedded WebView2), and Kick (PKCE; requires the user's own app client secret, protected with DPAPI). YouTube does not require Google sign-in.
- Normalized feed that automatically stays scrolled to the newest message, platform SVG marks, Twitch native emotes plus optional global/channel 7TV, BetterTTV, and FrankerFaceZ emotes, custom CSS injection and file hot reload, capped feed, and message deduplication. The **Sleek Dark** built-in theme is an original adaptation of the supplied visual reference for this overlay's HTML; it does not depend on the archive's OneComme markup or remote imports.
- Inline event alerts for Twitch IRC subscriptions, resubs, gift subs, raids, and cheers; YouTube Super Chats, Super Stickers, and memberships; and supported Twitch/YouTube/Kick events relayed by Streamer.bot. Twitch follows require the Streamer.bot relay; Facebook's comments source does not expose follow/subscription alerts.
- Memory-conscious feed retention: only the newest 40 rows remain in the renderer; Facebook keeps a bounded recent-comment dedupe window, and third-party emotes are not loaded when both Twitch and Streamer.bot feeds are disabled.
- Single-instance startup prevents duplicate overlay/WebView2 runtimes from multiplying memory use; launching the app again reveals the existing overlay. Emote providers load sequentially to reduce peak startup memory, and emote matching avoids dynamically compiled regex code to reduce retained runtime memory.
- On exit, chat sources are stopped and the WPF WebView2 controls (including the temporary Facebook sign-in view) are explicitly disposed so their owned browser controllers and runtime processes can shut down cleanly.
- Windows DPAPI protection for saved OAuth access/refresh tokens and platform client secrets. Provider Client IDs are stored separately in a dedicated `.env` file.
- Serilog rolling logs under `%AppData%\MultiChatOverlay\logs`.

The settings tabs intentionally expose current capabilities and identify unfinished areas. This is a working foundation, not a claim that every feature in the original specification is complete:

- Direct Kick chat, full badge imagery, OAuth browser flows/refresh, configurable hotkeys, full appearance/event controls, Streamer.bot button/chat UI, and the Facebook hidden-WebView fallback are not implemented.
- `StreamerBotSource` provides `SendMessageAsync` and `ExecuteActionAsync` for callers; the settings dialog does not yet expose message composition or action buttons.
- Streamer.bot password authentication is not sent: the official request/event references document the WebSocket request protocol but do not define a password handshake. Keep its WebSocket bound to localhost or a trusted network.
- YouTube chat relies on an undocumented, unofficial InnerTube interface and can stop working if YouTube changes it. It is read-only and has no YouTube Data API quota usage.
- The overlay is intended for windowed and borderless-windowed games on a single monitor. Exclusive fullscreen is unsupported.

## Build, run, and install

Requirements: Windows 10/11, .NET 8 SDK, and Inno Setup 6 to compile the installer.

```powershell
dotnet restore .\MultiChatOverlay.sln
dotnet build .\MultiChatOverlay.sln -c Release
dotnet test .\MultiChatOverlay.Tests\MultiChatOverlay.Tests.csproj -c Release
.\publish.ps1
```

`publish.ps1` publishes a self-contained `win-x64` application and downloads Microsoft's Evergreen WebView2 bootstrapper into `publish\`. Open `installer\MultiChatOverlay.iss` with Inno Setup 6 (or run `ISCC.exe installer\MultiChatOverlay.iss`). The installer runs the WebView2 bootstrapper silently if the runtime is not found.

The app writes settings to `%AppData%\MultiChatOverlay\settings.json` and OAuth Client IDs to `%AppData%\MultiChatOverlay\.env`. OAuth tokens and client secrets are encrypted with DPAPI for the current Windows user. Client IDs are public identifiers, not secrets; the `.env` file is plain text and contains only Client IDs. Logs are written to `%AppData%\MultiChatOverlay\logs`.

OAuth Client IDs are not shown in the app's settings interface. The app creates `%AppData%\MultiChatOverlay\.env` on first launch, with these four supported keys. `GOOGLE_CLIENT_ID` is retained for compatibility with existing configurations but is no longer used for YouTube chat:

```dotenv
TWITCH_CLIENT_ID=
GOOGLE_CLIENT_ID=
KICK_CLIENT_ID=
FACEBOOK_APP_ID=
```

Close the app before editing `.env`, add the Client IDs after `=`, and restart it. On first launch after this update, any Client IDs previously saved in `settings.json` are migrated into `.env` and removed from the settings file. Do not put access tokens, client secrets, or other credentials in `.env`.

## Platform setup

### Twitch

1. Sign in at the [Twitch Developer Console](https://dev.twitch.tv/console/apps) and choose **Register Your Application**. Select a desktop/chat use case and copy the app's **Client ID** (the app does not need its client secret for device-code sign-in).
2. Set `TWITCH_CLIENT_ID` in `.env`, restart the app, then in **Settings → Connections**, choose **Sign in to Twitch** and complete the one-time device code in the browser. The verified Twitch login and avatar are shown; the login is used as the chat channel by default. Choose **Log off** to revoke and remove the saved Twitch sign-in. Uncheck **Automatically use my signed-in Twitch account as the channel** to enter a different channel.
3. This build only reads chat; it does not send IRC messages. Twitch IRC access is authenticated according to current Twitch docs, so there is no anonymous-read mode.

### YouTube

1. In **Settings → Platforms → YouTube**, enter a live video URL or video ID (not the API's `liveChatId`). You can also enter a channel `@handle`; that only attaches when the library can find an active chat and does not continuously monitor for future streams.
2. Save the settings. No Google Cloud project, Client ID, API key, or sign-in is needed. The source reads chat through YTLiveChat's unofficial InnerTube implementation; it is read-only, uses no YouTube Data API quota, and may need updates if YouTube changes its internal interface. Restricted chats may not be accessible.

### Kick

1. Follow Kick's [app setup guide](https://docs.kick.com/getting-started/kick-apps-setup.md) to create a developer app. Register `http://localhost:43821/callback/` as its exact redirect URI. Copy the app's Client ID and client secret.
2. Set `KICK_CLIENT_ID` in `.env` and restart the app. In **Settings → Platforms**, enter the client secret and choose **Sign in to Kick**. The client secret is encrypted with Windows DPAPI for the current Windows user; it is not embedded in the application.
3. Kick chat events still arrive through Streamer.bot in this build. Direct Kick webhook ingestion is not included because its official webhook API requires a publicly reachable callback URL; there is no fabricated local Pusher endpoint.

### Facebook

1. In [Meta for Developers](https://developers.facebook.com/apps/), create/select an app and add the **Facebook Login** product. In Facebook Login settings, add `https://www.facebook.com/connect/login_success.html` to **Valid OAuth Redirect URIs**. Copy the app's **App ID** (not the App Secret).
2. Configure the Page/live-video access and permissions needed for your Page; Meta may require app review or business verification. See [Meta's manual Login flow](https://developers.facebook.com/docs/facebook-login/guides/advanced/manual-flow/) and the [live-video comments reference](https://developers.facebook.com/docs/graph-api/reference/live-video/comments/).
3. Set `FACEBOOK_APP_ID` in `.env` and restart the app. In **Settings → Platforms**, enter the live-video ID and choose **Sign in to Facebook**. Facebook Login opens in an embedded WebView2 and requests page read permissions. The resulting access token is DPAPI protected. The Facebook App Secret is not requested or embedded.

### Third-party emotes

Open **Settings → Emotes** to independently enable or disable 7TV, BetterTTV, and FrankerFaceZ. Enabled providers load their global and Twitch-channel emotes when the app starts and match emotes even when adjacent to punctuation; Twitch's native emotes are unaffected. A Twitch channel and signed-in Twitch account are needed to resolve channel-specific 7TV and BetterTTV sets. Provider responses are cached under `%AppData%\MultiChatOverlay\emote-cache` and reused if a refresh fails. The log records how many emotes loaded from each provider; if the channel sets are missing, check the log for channel-ID lookup errors. Third-party APIs: [7TV API](https://7tv.io/docs), [BetterTTV API](https://github.com/BetterTTV/BetterTTV), [FrankerFaceZ API](https://www.frankerfacez.com/developers).
3. This build polls Graph API comments every five seconds and ignores the initial page of existing comments. The hidden-WebView DOM-observer fallback is not included; Graph permissions/API changes may prevent access.

### Streamer.bot

1. In Streamer.bot, enable its WebSocket server and note the host/port (default in this app: `ws://127.0.0.1:8080/`).
2. Enable **Settings → Streamer.bot → Connect** only if you want Streamer.bot-relayed events. Twitch channel-point redemptions are received directly through EventSub and do not require Streamer.bot.
3. The connector supports `DoAction` by action name and `SendMessage` for Twitch/Kick/YouTube programmatically. Add Streamer.bot actions and platform permissions there before calling these methods from an extension.
4. Streamer.bot event relay and native sources can deliver duplicate messages; the default dedupe uses `(platform, message ID)`. Disable dedupe in General if this is undesirable. IDs that differ between sources cannot be recognized as duplicates.

## Hotkeys

| Hotkey | Action |
|---|---|
| `Ctrl+Shift+L` | Lock/unlock; locked mode is fully click-through |
| `Ctrl+Shift+O` | Show/hide |
| `Ctrl+Shift+C` | Clear feed |
| `Ctrl+Shift+Up` | Increase opacity |
| `Ctrl+Shift+Down` | Decrease opacity |

These are fixed in this build. If a hotkey is already registered by another app, Windows may reject it; choose a different key in a future configurable hotkey UI.

## Custom renderer CSS

`MultiChatOverlay.App\Assets\chat.html` is the renderer page and `default.css` is its base theme. The feed receives normalized JSON through WebView2 `PostWebMessageAsJson`. Edit the CSS file in Settings or provide a custom `.css` file; file changes are watched and reloaded.

Supported classes and elements:

| Selector | Purpose |
|---|---|
| `.message` | Chat message row |
| `.platform-icon` | Inline SVG source mark; platform classes use `--platform-twitch`, `--platform-youtube`, `--platform-kick`, `--platform-facebook`, and `--platform-streamerbot` color variables |
| `.username` | Colored display name |
| `.badge`, `.badge-subscriber`, `.badge-broadcaster`, `.badge-moderator` | Inline SVG subscriber, broadcaster, and moderator marks |
| `.message-text` | Message content |
| `.emote` | Inline emote image |
| `.timestamp` | Reserved for timestamps |
| `.event` | Follow/sub/gift/raid/donation/cheer/Super Chat row |
| `.event-icon` | Event type label |
| `.twitch`, `.youtube`, `.kick`, `.facebook`, `.streamerbot` | Platform modifier classes |

WebView2 inserts message text using text nodes; message content is not interpreted as HTML. Messages from all enabled platforms share one feed, are ordered by their source timestamps, and the newest row stays visible when the window is resized. The renderer does not add simulated sample messages.

## Troubleshooting

- **Blank renderer / WebView2 error:** Install or repair the Evergreen WebView2 Runtime, then restart.
- **Twitch does not connect:** Check the channel/account logins and `chat:read` access. The app validates saved tokens on startup. If Twitch rejects an expired or revoked token, sign in again in Settings; Twitch device-code sessions may not be refreshable. Hover the Twitch status indicator for the latest connection error, or inspect the rolling log.
- **YouTube chat is missing:** Confirm chat is enabled and enter the stream's video URL or video ID (not the YouTube Data API `liveChatId`); restricted chats may not be accessible to YTLiveChat.
- **Facebook returns permission errors:** Confirm the Page/live-video IDs and app/Page permissions; a user token may not be sufficient.
- **Streamer.bot shows disconnected:** Check the configured host/port, server enabled state, firewall, and subscriptions in the Streamer.bot application.
- **Twitch channel point redemptions are missing:** Sign in to Twitch again in Settings and grant `channel:read:redemptions`. The signed-in Twitch account must own the configured channel. Redemption events are read directly through Twitch EventSub; Streamer.bot is optional.
- **Overlay does not appear over a game:** Use windowed or borderless-windowed mode. Exclusive fullscreen is unsupported.
- **App is blocked from running tests:** Some managed Windows environments enforce Application Control policies that block test-host assemblies; build success does not override that system policy.
