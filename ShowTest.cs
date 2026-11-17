using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace WordPeek;

/// <summary>Показывает плашку в заданной точке и держит её указанное время — для проверки на экране.</summary>
internal static class ShowTest
{
    public static void Run(string? secondsArgument)
    {
        int seconds = 12;
        if (int.TryParse(secondsArgument, out int parsed))
        {
            seconds = Math.Clamp(parsed, 2, 120);
        }

        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };

        app.Startup += (_, _) =>
        {
            var overlay = new OverlayWindow();
            overlay.ShowTranslation("Hello, how are you today?", 600, 400, 20);
            Log.Write("showtest shown");

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                app.Shutdown();
            };
            timer.Start();
        };

        app.Run();
    }
}
