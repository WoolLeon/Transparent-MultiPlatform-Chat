namespace MultiChatOverlay.Core.Models;

public sealed record ChatItem
{
    private ChatItem(ChatMessage? message, ChatEvent? chatEvent)
    {
        Message = message;
        Event = chatEvent;
    }

    public ChatMessage? Message { get; }
    public ChatEvent? Event { get; }

    public static ChatItem FromMessage(ChatMessage message) => new(message, null);
    public static ChatItem FromEvent(ChatEvent chatEvent) => new(null, chatEvent);
}
