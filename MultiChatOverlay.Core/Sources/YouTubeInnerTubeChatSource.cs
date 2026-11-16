using System.Globalization;
using System.Net.Http;
using MultiChatOverlay.Core.Models;
using YTLiveChat.Contracts;
using YTLiveChat.Contracts.Models;
using YTLiveChat.Contracts.Services;
using YTLiveChat.Services;
using YtChatService = YTLiveChat.Services.YTLiveChat;
using UnifiedChatBadge = MultiChatOverlay.Core.Models.ChatBadge;
using UnifiedChatEvent = MultiChatOverlay.Core.Models.ChatEvent;
using UnifiedChatItem = MultiChatOverlay.Core.Models.ChatItem;
using UnifiedChatMessage = MultiChatOverlay.Core.Models.ChatMessage;
using UnifiedEmotePart = MultiChatOverlay.Core.Models.EmotePart;
using UnifiedTextPart = MultiChatOverlay.Core.Models.TextPart;
using YtChatItem = YTLiveChat.Contracts.Models.ChatItem;
using YtEmojiPart = YTLiveChat.Contracts.Models.EmojiPart;
using YtImagePart = YTLiveChat.Contracts.Models.ImagePart;
using YtMessagePart = YTLiveChat.Contracts.Models.MessagePart;
using YtTextPart = YTLiveChat.Contracts.Models.TextPart;

namespace MultiChatOverlay.Core.Sources;

public sealed class YouTubeInnerTubeChatSource(string target) : IChatSource
{
    private readonly string _target = ValidateTarget(target);
    private HttpClient? _httpClient;
    private IYTLiveChat? _chat;
    private bool _stopping;
    private ConnectionState _state = ConnectionState.Disconnected;

    public ChatPlatform Platform => ChatPlatform.YouTube;
    public ConnectionState State => _state;
    public event Action<UnifiedChatItem>? ItemReceived;
    public event Action<ConnectionState>? StateChanged;
    public event Action<Exception>? ErrorOccurred;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_chat is not null)
            return Task.CompletedTask;

        _stopping = false;
        _httpClient = new HttpClient { BaseAddress = new Uri("https://www.youtube.com") };
        _chat = new YtChatService(new YTLiveChatOptions(), new YTHttpClient(_httpClient));
        _chat.InitialPageLoaded += (_, _) => SetState(ConnectionState.Connected);
        _chat.ChatReceived += (_, args) =>
        {
            var item = ConvertItem(args.ChatItem);
            if (item is not null)
                ItemReceived?.Invoke(item);
        };
        _chat.ErrorOccurred += (_, args) =>
        {
            ErrorOccurred?.Invoke(args.GetException());
            SetState(ConnectionState.Reconnecting);
        };
        _chat.ChatStopped += (_, _) =>
        {
            if (!_stopping)
                SetState(ConnectionState.Disconnected);
        };

        SetState(ConnectionState.Connecting);
        try
        {
            if (_target.StartsWith('@'))
                _chat.Start(handle: _target);
            else
                _chat.Start(liveId: _target);
        }
        catch (Exception ex)
        {
            SetState(ConnectionState.Faulted);
            ErrorOccurred?.Invoke(ex);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _stopping = true;
        _chat?.Stop();
        _chat?.Dispose();
        _chat = null;
        _httpClient?.Dispose();
        _httpClient = null;
        SetState(ConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private void SetState(ConnectionState state)
    {
        if (_state == state)
            return;

        _state = state;
        StateChanged?.Invoke(state);
    }

    private static UnifiedChatItem? ConvertItem(YtChatItem item)
    {
        var message = string.Concat(item.Message.Select(GetPartText));
        if (item.Superchat is { } superchat)
        {
            return UnifiedChatItem.FromEvent(new UnifiedChatEvent
            {
                Id = item.Id,
                Type = "superchat",
                Platform = ChatPlatform.YouTube,
                User = item.Author.Name,
                Amount = superchat.AmountString,
                Message = message,
                Timestamp = item.Timestamp
            });
        }

        if (item.MembershipDetails is { } membership)
        {
            var eventType = membership.EventType switch
            {
                MembershipEventType.GiftPurchase or MembershipEventType.GiftRedemption => "giftsub",
                MembershipEventType.Milestone => "resub",
                _ => "membership"
            };
            var amount = membership.GiftCount?.ToString(CultureInfo.InvariantCulture)
                         ?? membership.MilestoneMonths?.ToString(CultureInfo.InvariantCulture);
            var eventMessage = string.Join(
                " ",
                new[] { membership.HeaderPrimaryText, membership.HeaderSubtext, message }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal));

            return UnifiedChatItem.FromEvent(new UnifiedChatEvent
            {
                Id = item.Id,
                Type = eventType,
                Platform = ChatPlatform.YouTube,
                User = item.Author.Name,
                Amount = amount,
                Message = eventMessage,
                Timestamp = item.Timestamp
            });
        }

        var messageParts = item.Message.Select(ConvertPart).Where(part => part is not null).Cast<ChatPart>().ToArray();
        if (messageParts.Length == 0)
            return null;

        var badges = item.Author.Badge is { } badge
            ? new[] { new UnifiedChatBadge(badge.Label, string.Empty, badge.Thumbnail?.Url) }
            : [];
        return UnifiedChatItem.FromMessage(new UnifiedChatMessage
        {
            Id = item.Id,
            Platform = ChatPlatform.YouTube,
            Username = string.IsNullOrWhiteSpace(item.Author.ChannelHandle)
                ? string.IsNullOrWhiteSpace(item.Author.ChannelId) ? item.Author.Name : item.Author.ChannelId
                : item.Author.ChannelHandle,
            DisplayName = item.Author.Name,
            Badges = badges,
            MessageParts = messageParts,
            Timestamp = item.Timestamp,
            IsModerator = item.IsModerator,
            IsSubscriber = item.IsMembership,
            IsOwner = item.IsOwner
        });
    }

    private static ChatPart? ConvertPart(YtMessagePart part) => part switch
    {
        YtTextPart text => new UnifiedTextPart(text.Text),
        YtEmojiPart emoji when !string.IsNullOrWhiteSpace(emoji.Url) =>
            new UnifiedEmotePart(emoji.EmojiText, emoji.Url, "youtube"),
        YtImagePart image when !string.IsNullOrWhiteSpace(image.Url) =>
            new UnifiedEmotePart(image.Alt ?? string.Empty, image.Url, "youtube"),
        _ => null
    };

    private static string GetPartText(YtMessagePart part) => part switch
    {
        YtTextPart text => text.Text,
        YtEmojiPart emoji => emoji.EmojiText,
        YtImagePart image => image.Alt ?? string.Empty,
        _ => string.Empty
    };

    private static string ValidateTarget(string value)
    {
        var target = value?.Trim() ?? string.Empty;
        if (target.Length == 0)
            throw new ArgumentException("Enter a YouTube video ID or channel handle.", nameof(value));

        if (Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
                target = uri.AbsolutePath.Trim('/');
            else if (uri.Query.Length > 0)
            {
                var videoId = uri.Query.TrimStart('?')
                    .Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Split('=', 2))
                    .FirstOrDefault(pair => pair.Length == 2 && pair[0] == "v");
                if (videoId is not null)
                    target = Uri.UnescapeDataString(videoId[1]);
            }
            else
            {
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var videoSegment = Array.FindIndex(segments, segment =>
                    segment is "live" or "shorts" or "embed");
                if (videoSegment >= 0 && videoSegment + 1 < segments.Length)
                    target = segments[videoSegment + 1];
                else
                {
                    var handle = segments.FirstOrDefault(segment => segment.StartsWith('@'));
                    if (handle is not null)
                        target = handle;
                }
            }
        }

        if (target.Length == 0)
            throw new ArgumentException("Could not find a YouTube video ID or channel handle in the supplied value.", nameof(value));

        return target;
    }
}
