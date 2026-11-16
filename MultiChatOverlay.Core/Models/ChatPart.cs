using System.Text.Json.Serialization;

namespace MultiChatOverlay.Core.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TextPart), "text")]
[JsonDerivedType(typeof(EmotePart), "emote")]
public abstract record ChatPart;

public sealed record TextPart(string Text) : ChatPart;

public sealed record EmotePart(string Name, string Url, string Provider) : ChatPart;

public sealed record ChatBadge(string Name, string Version, string? ImageUrl = null);
