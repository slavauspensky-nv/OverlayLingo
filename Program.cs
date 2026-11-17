using System.IO;
using System.Text;
using System.Windows;

namespace WordPeek;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].StartsWith("--probe", StringComparison.OrdinalIgnoreCase))
        {
            Probe.Run(args.Length > 1 ? args[1] : null);
            return;
        }

        if (args.Length > 0 && args[0].StartsWith("--demo", StringComparison.OrdinalIgnoreCase))
        {
            Demo.Run();
            return;
        }

        if (args.Length > 0 && args[0].StartsWith("--selftest", StringComparison.OrdinalIgnoreCase))
        {
            SelfTest.Run();
            return;
        }

        if (args.Length > 0 && args[0].StartsWith("--icontest", StringComparison.OrdinalIgnoreCase))
        {
            string path = Path.Combine(AppContext.BaseDirectory, "trayicon.png");
            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "icontest.log"),
                TrayController.SavePreview(path));
            return;
        }

        if (args.Length > 0 && args[0].StartsWith("--parsetest", StringComparison.OrdinalIgnoreCase))
        {
            ParseTest.Run();
            return;
        }

        if (args.Length > 0 && args[0].StartsWith("--showtest", StringComparison.OrdinalIgnoreCase))
        {
            ShowTest.Run(args.Length > 1 ? args[1] : null);
            return;
        }

        if (args.Length > 0 && args[0].StartsWith("--quiztest", StringComparison.OrdinalIgnoreCase))
        {
            QuizTest.Run();
            return;
        }

        if (args.Length > 0 && args[0].StartsWith("--wordtest", StringComparison.OrdinalIgnoreCase))
        {
            WordTest.Run();
            return;
        }

        if (args.Length > 0 && args[0].StartsWith("--soundtest", StringComparison.OrdinalIgnoreCase))
        {
            SoundTest.Run();
            return;
        }

        if (args.Length > 0 && args[0].StartsWith("--fonttest", StringComparison.OrdinalIgnoreCase))
        {
            FontTest.Run();
            return;
        }

        if (args.Length > 0 && args[0].StartsWith("--notetest", StringComparison.OrdinalIgnoreCase))
        {
            NoteTest.Run();
            return;
        }

        if (args.Length > 0 && args[0].StartsWith("--shottest", StringComparison.OrdinalIgnoreCase))
        {
            ShotTest.Run(args.Length > 1 ? args[1] : null);
            return;
        }

        if (args.Length > 0 && args[0].StartsWith("--fieldtest", StringComparison.OrdinalIgnoreCase))
        {
            FieldTest.Run(args.Length > 1 ? args[1] : null);
            return;
        }

        if (args.Length > 0 && args[0].StartsWith("--hooktest", StringComparison.OrdinalIgnoreCase))
        {
            HookTest.Run();
            return;
        }

        if (args.Length > 0 && args[0].StartsWith("--tree", StringComparison.OrdinalIgnoreCase))
        {
            TreeDump.Run(args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null);
            return;
        }

        if (args.Length > 0 && args[0].StartsWith("--translate", StringComparison.OrdinalIgnoreCase))
        {
            TranslateTest.Run(args.Length > 1 ? args[1] : null);
            return;
        }

        // Первая строка в логе: если её нет, процесс вообще не стартовал.
        Log.Write($"main: старт, аргументы=[{string.Join(" ", args)}]");

        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };

        // Любая необработанная ошибка должна попасть в лог, а не убить приложение.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Write("FATAL: " + e.ExceptionObject);

        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Write("UI FATAL: " + e.Exception);
            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Write("TASK FATAL: " + e.Exception);
            e.SetObserved();
        };

        // Второй экземпляр не нужен: два наблюдателя и две плашки только мешают.
        // Берём мьютекс через WaitOne — так он корректно освобождается даже после
        // убитого предыдущего процесса.
        using var singleInstance = new Mutex(false, "WordPeek.SingleInstance");
        bool owned;

        try
        {
            owned = singleInstance.WaitOne(0, false);
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }

        if (!owned)
        {
            Log.Write("main: уже запущен другой экземпляр, выходим");
            return;
        }

        Log.Write("main: экземпляр единственный, продолжаем");

        app.Startup += (_, _) =>
        {
            try
            {
                var controller = new AppController();
                controller.Start();
            }
            catch (Exception ex)
            {
                Log.Write("startup: " + ex);
                MessageBox.Show(ex.ToString(), "WordPeek — ошибка запуска");
                app.Shutdown(1);
            }
        };

        app.Run();
    }
}

internal static class Probe
{
    public static void Run(string? secondsArgument)
    {
        // В приложении чтение идёт из MTA-потока — проверяем именно этот режим.
        var thread = new Thread(() => Loop(secondsArgument))
        {
            IsBackground = true,
            Name = "probe-mta"
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
    }

    private static void Loop(string? secondsArgument)
    {
        int seconds = 30;
        if (int.TryParse(secondsArgument, out int parsed))
        {
            seconds = Math.Clamp(parsed, 3, 600);
        }

        string path = Path.Combine(AppContext.BaseDirectory, "probe.log");
        var log = new StringBuilder();
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);

        while (DateTime.UtcNow < end)
        {
            FieldSnapshot snapshot;
            try
            {
                snapshot = FieldReader.Read();
            }
            catch (Exception ex)
            {
                log.AppendLine($"{DateTime.Now:HH:mm:ss.fff} ERROR {ex.Message}");
                continue;
            }

            string text = snapshot.Text.Replace("\r", " ").Replace("\n", " ");
            if (text.Length > 140)
            {
                text = text[..140] + "…";
            }

            log.AppendLine(
                $"{DateTime.Now:HH:mm:ss.fff} field={snapshot.IsTextField} pwd={snapshot.IsPassword} " +
                $"caret={snapshot.HasCaret} ({snapshot.CaretX},{snapshot.CaretY},{snapshot.CaretH}) " +
                $"anchor=({snapshot.AnchorX},{snapshot.AnchorY}) " +
                $"type={snapshot.ControlTypeName} proc={snapshot.ProcessName} text=\"{text}\"");

            try
            {
                File.WriteAllText(path, log.ToString());
            }
            catch
            {
            }

            Thread.Sleep(400);
        }

        try
        {
            File.AppendAllText(path, "=== probe finished ===\r\n");
        }
        catch
        {
        }
    }
}
