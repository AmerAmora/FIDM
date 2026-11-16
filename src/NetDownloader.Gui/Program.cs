using System;
using System.IO;
using Avalonia;

namespace NetDownloader.Gui;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup.log");
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try { File.AppendAllText(logPath, $"UNHANDLED: {e.ExceptionObject}\n"); } catch { }
        };
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            try { File.AppendAllText(logPath, $"UNOBSERVED: {e.Exception}\n"); } catch { }
        };

        try
        {
            File.AppendAllText(logPath, $"Starting app at {DateTime.Now}...\n");
            int code = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            File.AppendAllText(logPath, $"App exited with code {code} at {DateTime.Now}\n");
        }
        catch (Exception ex)
        {
            File.AppendAllText(logPath, $"App crashed: {ex}\n");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
