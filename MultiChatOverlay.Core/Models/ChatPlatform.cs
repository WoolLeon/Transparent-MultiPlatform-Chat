using System.Text.Json.Serialization;

namespace MultiChatOverlay.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChatPlatform
{
    Twitch,
    YouTube,
    Kick,
    Facebook,
    StreamerBot
}
