namespace MultiChatOverlay.Core.Models;

public sealed record ChatItem
{
    private ChatItem(ChatMessage? message, ChatEvent? chatEvent, ChatItemSource source)
    {
        Message = message;
        Event = chatEvent;
        Source = source;
    }

    public ChatMessage? Message { get; }
    public ChatEvent? Event { get; }
    public ChatItemSource Source { get; }

    public static ChatItem FromMessage(ChatMessage message, ChatItemSource source = ChatItemSource.Native) =>
        new(message, null, source);

    public static ChatItem FromEvent(ChatEvent chatEvent, ChatItemSource source = ChatItemSource.Native) =>
        new(null, chatEvent, source);
}
