using Serilog;
using System.IO;

namespace MultiChatOverlay.App.Logging;

public static class Log
{
    static Log()
    {
        Serilog.Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MultiChatOverlay", "logs", "overlay-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true)
            .CreateLogger();
    }

    public static ILogger Logger => Serilog.Log.Logger;
}
