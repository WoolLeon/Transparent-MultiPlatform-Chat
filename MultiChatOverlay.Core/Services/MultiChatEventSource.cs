using System.Diagnostics.Tracing;

namespace MultiChatOverlay.Core.Services;

[EventSource(Name = "MultiChatOverlay")]
public sealed class MultiChatEventSource : EventSource
{
    public static readonly MultiChatEventSource Log = new();

    private MultiChatEventSource() { }

    // Keywords for filtering
    public static class Keywords
    {
        public const EventKeywords Connection = (EventKeywords)1;
        public const EventKeywords Messages = (EventKeywords)2;
        public const EventKeywords Emotes = (EventKeywords)4;
        public const EventKeywords Errors = (EventKeywords)8;
        public const EventKeywords Performance = (EventKeywords)16;
    }

    // Tasks for grouping
    public static class Tasks
    {
        public const EventTask Connection = (EventTask)1;
        public const EventTask MessageProcessing = (EventTask)2;
        public const EventTask EmoteLoading = (EventTask)3;
        public const EventTask Shutdown = (EventTask)4;
    }

    #region Connection Events

    [Event(1, Level = EventLevel.Informational, Keywords = Keywords.Connection, Task = Tasks.Connection)]
    public void SourceConnecting(string platform, string detail)
    {
        WriteEvent(1, platform, detail);
    }

    [Event(2, Level = EventLevel.Informational, Keywords = Keywords.Connection, Task = Tasks.Connection)]
    public void SourceConnected(string platform, string channel)
    {
        WriteEvent(2, platform, channel);
    }

    [Event(3, Level = EventLevel.Warning, Keywords = Keywords.Connection | Keywords.Errors, Task = Tasks.Connection)]
    public void SourceDisconnected(string platform, string reason)
    {
        WriteEvent(3, platform, reason);
    }

    [Event(4, Level = EventLevel.Error, Keywords = Keywords.Connection | Keywords.Errors, Task = Tasks.Connection)]
    public void SourceFailed(string platform, string error)
    {
        WriteEvent(4, platform, error);
    }

    [Event(5, Level = EventLevel.Informational, Keywords = Keywords.Connection, Task = Tasks.Connection)]
    public void SourceReconnecting(string platform, int attempt, double nextRetrySeconds)
    {
        WriteEvent(5, platform, attempt, nextRetrySeconds);
    }

    [Event(6, Level = EventLevel.Verbose, Keywords = Keywords.Connection | Keywords.Performance, Task = Tasks.Connection)]
    public void ConnectionLatency(string platform, double latencyMs)
    {
        WriteEvent(6, platform, latencyMs);
    }

    #endregion

    #region Message Events

    [Event(10, Level = EventLevel.Informational, Keywords = Keywords.Messages, Task = Tasks.MessageProcessing)]
    public void MessageReceived(string platform, string username, string displayName, string messageId)
    {
        WriteEvent(10, platform, username, displayName, messageId);
    }

    [Event(11, Level = EventLevel.Verbose, Keywords = Keywords.Messages | Keywords.Performance, Task = Tasks.MessageProcessing)]
    public void MessageProcessed(string platform, int messagePartsCount, int emoteCount, double processingMs)
    {
        WriteEvent(11, platform, messagePartsCount, emoteCount, processingMs);
    }

    [Event(12, Level = EventLevel.Informational, Keywords = Keywords.Messages, Task = Tasks.MessageProcessing)]
    public void MessageDeduplicated(string platform, string messageId, string reason)
    {
        WriteEvent(12, platform, messageId, reason);
    }

    [Event(13, Level = EventLevel.Informational, Keywords = Keywords.Messages, Task = Tasks.MessageProcessing)]
    public void EventReceived(string platform, string eventType, string user, string eventId)
    {
        WriteEvent(13, platform, eventType, user, eventId);
    }

    [Event(14, Level = EventLevel.Informational, Keywords = Keywords.Messages, Task = Tasks.MessageProcessing)]
    public void MessageFiltered(string platform, string username, string reason)
    {
        WriteEvent(14, platform, username, reason);
    }

    #endregion

    #region Emote Events

    [Event(20, Level = EventLevel.Informational, Keywords = Keywords.Emotes, Task = Tasks.EmoteLoading)]
    public void EmoteProviderLoaded(string provider, int count)
    {
        WriteEvent(20, provider, count);
    }

    [Event(21, Level = EventLevel.Warning, Keywords = Keywords.Emotes | Keywords.Errors, Task = Tasks.EmoteLoading)]
    public void EmoteProviderFailed(string provider, string error)
    {
        WriteEvent(21, provider, error);
    }

    [Event(22, Level = EventLevel.Informational, Keywords = Keywords.Emotes, Task = Tasks.EmoteLoading)]
    public void EmoteCacheHit(string provider, string cachePath)
    {
        WriteEvent(22, provider, cachePath);
    }

    [Event(23, Level = EventLevel.Verbose, Keywords = Keywords.Emotes | Keywords.Performance, Task = Tasks.EmoteLoading)]
    public void EmoteLoadDuration(string provider, double durationMs, bool fromCache)
    {
        WriteEvent(23, provider, durationMs, fromCache);
    }

    #endregion

    #region Application Events

    [Event(30, Level = EventLevel.Informational, Keywords = Keywords.Connection, Task = Tasks.Shutdown)]
    public void ApplicationStarting(string version)
    {
        WriteEvent(30, version);
    }

    [Event(31, Level = EventLevel.Informational, Keywords = Keywords.Connection, Task = Tasks.Shutdown)]
    public void ApplicationShuttingDown(string reason)
    {
        WriteEvent(31, reason);
    }

    [Event(32, Level = EventLevel.Error, Keywords = Keywords.Errors, Task = Tasks.Shutdown)]
    public void UnhandledException(string exceptionType, string message, string stackTrace)
    {
        WriteEvent(32, exceptionType, message, stackTrace);
    }

    #endregion

    #region Performance Events

    [Event(40, Level = EventLevel.Verbose, Keywords = Keywords.Performance, Task = Tasks.MessageProcessing)]
    public void WebViewMessageBatched(int count, double flushMs)
    {
        WriteEvent(40, count, flushMs);
    }

    [Event(41, Level = EventLevel.Verbose, Keywords = Keywords.Performance, Task = Tasks.MessageProcessing)]
    public void WebViewMessageSent(int count, double sendMs)
    {
        WriteEvent(41, count, sendMs);
    }

    [Event(42, Level = EventLevel.Verbose, Keywords = Keywords.Performance, Task = Tasks.MessageProcessing)]
    public void ChatRenderDuration(double renderMs, int visibleMessages)
    {
        WriteEvent(42, renderMs, visibleMessages);
    }

    #endregion
}