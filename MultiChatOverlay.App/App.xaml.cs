using System.Windows;
using System.Runtime.InteropServices;
using System.Threading;
using MultiChatOverlay.App.Logging;

namespace MultiChatOverlay.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\MultiChatOverlay.SingleInstance.v2";
    private const int WmShowOverlay = 0x8000 + 0x351;
    private const int SwShowNoActivate = 8;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Force logger initialization early
        _ = Log.Logger;

        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out _ownsSingleInstanceMutex);
        }
        catch (AbandonedMutexException)
        {
            // Previous instance crashed - we now own the mutex
            _ownsSingleInstanceMutex = true;
        }

        if (!_ownsSingleInstanceMutex)
        {
            var existingWindow = FindWindow(null, "Multi Chat Overlay");
            if (existingWindow != IntPtr.Zero)
            {
                ShowWindowAsync(existingWindow, SwShowNoActivate);
                PostMessage(existingWindow, WmShowOverlay, IntPtr.Zero, IntPtr.Zero);
            }

            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Logger.Error(args.Exception, "Unhandled UI exception");
            System.Windows.MessageBox.Show("The overlay encountered an error. Details were written to the log file.",
                "Multi Chat Overlay", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                Log.Logger.Fatal(exception, "Unhandled process exception");
        };

        Log.Logger.Information("Application starting up");
        MainWindow = new MainWindow();
        Log.Logger.Information("MainWindow created, showing");
        MainWindow.Show();
        Log.Logger.Information("MainWindow shown");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsSingleInstanceMutex)
            _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        Serilog.Log.CloseAndFlush();
        base.OnExit(e);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);
}
