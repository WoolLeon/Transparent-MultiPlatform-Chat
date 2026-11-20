using System.Text;
using System.Text.Json;
using MultiChatOverlay.Core.Models;

namespace MultiChatOverlay.Core.Services;

public sealed class ChatLogExporter : IDisposable
{
    private readonly object _gate = new();
    private StreamWriter? _jsonlWriter;
    private StreamWriter? _csvWriter;
    private string? _jsonlPath;
    private string? _csvPath;
    private long _messageCount;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public string? JsonlPath => _jsonlPath;
    public string? CsvPath => _csvPath;
    public long MessageCount => _messageCount;
    public bool IsEnabled => _jsonlWriter != null || _csvWriter != null;

    public void Start(string? jsonlPath = null, string? csvPath = null)
    {
        lock (_gate)
        {
            Stop();

            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MultiChatOverlay",
                "chat-logs");

            Directory.CreateDirectory(baseDir);

            if (jsonlPath != null || true) // Default to JSONL
            {
                _jsonlPath = jsonlPath ?? Path.Combine(baseDir, $"chat_{timestamp}.jsonl");
                _jsonlWriter = new StreamWriter(_jsonlPath, append: false, Encoding.UTF8);
                _jsonlWriter.AutoFlush = true;
            }

            if (csvPath != null)
            {
                _csvPath = csvPath;
                _csvWriter = new StreamWriter(_csvPath, append: false, Encoding.UTF8);
                _csvWriter.AutoFlush = true;
                WriteCsvHeader();
            }

            _messageCount = 0;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _jsonlWriter?.Dispose();
            _jsonlWriter = null;
            _csvWriter?.Dispose();
            _csvWriter = null;
        }
    }

    public void LogMessage(ChatMessage message)
    {
        lock (_gate)
        {
            if (_jsonlWriter != null)
            {
                var logEntry = new
                {
                    id = message.Id,
                    timestamp = message.Timestamp.ToString("O"),
                    platform = message.Platform.ToString(),
                    username = message.Username,
                    displayName = message.DisplayName,
                    userColor = message.UserColor,
                    badges = message.Badges.Select(b => new { b.Name, b.Version, b.ImageUrl }).ToArray(),
                    messageParts = message.MessageParts.Select<ChatPart, object>(p => p switch
                    {
                        TextPart t => new { kind = "text", text = t.Text },
                        EmotePart e => new { kind = "emote", name = e.Name, url = e.Url, provider = e.Provider },
                        _ => new { kind = "unknown" }
                    }).ToArray(),
                    isHighlighted = message.IsHighlighted,
                    isModerator = message.IsModerator,
                    isSubscriber = message.IsSubscriber,
                    isOwner = message.IsOwner
                };
                var json = JsonSerializer.Serialize(logEntry, _jsonOptions);
                _jsonlWriter.WriteLine(json);
            }

            if (_csvWriter != null)
            {
                var text = string.Concat(message.MessageParts.Select<ChatPart, string>(p => p switch
                {
                    TextPart t => t.Text,
                    EmotePart e => $"[{e.Name}]",
                    _ => ""
                }));

                var badges = string.Join(";", message.Badges.Select(b => $"{b.Name}:{b.Version}"));
                var escapedText = EscapeCsv(text);
                var escapedDisplayName = EscapeCsv(message.DisplayName);
                var escapedUsername = EscapeCsv(message.Username);

                _csvWriter.WriteLine($"{message.Timestamp:O},{message.Platform},{escapedUsername},{escapedDisplayName},{escapedText},{badges},{message.IsModerator},{message.IsSubscriber},{message.IsOwner},{message.IsHighlighted}");
            }

            _messageCount++;
        }
    }

    public void LogEvent(ChatEvent evt)
    {
        lock (_gate)
        {
            if (_jsonlWriter != null)
            {
                var logEntry = new
                {
                    id = evt.Id,
                    timestamp = evt.Timestamp.ToString("O"),
                    platform = evt.Platform.ToString(),
                    type = evt.Type,
                    user = evt.User,
                    amount = evt.Amount,
                    message = evt.Message
                };
                var json = JsonSerializer.Serialize(logEntry, _jsonOptions);
                _jsonlWriter.WriteLine(json);
            }

            if (_csvWriter != null)
            {
                var escapedUser = EscapeCsv(evt.User);
                var escapedMessage = EscapeCsv(evt.Message ?? "");
                var escapedAmount = EscapeCsv(evt.Amount ?? "");
                _csvWriter.WriteLine($"{evt.Timestamp:O},{evt.Platform},EVENT,{escapedUser},{escapedUser},{escapedMessage},,{escapedAmount},False,False,False,False");
            }

            _messageCount++;
        }
    }

    private void WriteCsvHeader()
    {
        if (_csvWriter != null)
        {
            _csvWriter.WriteLine("Timestamp,Platform,Username,DisplayName,Message,Badges,IsModerator,IsSubscriber,IsOwner,IsHighlighted");
        }
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    public void Dispose() => Stop();
}