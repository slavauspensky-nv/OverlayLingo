using System.IO;
using System.Windows.Threading;

namespace OverlayLingo;

/// <summary>
/// Опрашивает активное поле ввода в отдельном MTA-потоке и передаёт снимки в UI-поток.
/// </summary>
internal sealed class FieldWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private Thread? _thread;
    private volatile bool _stop;
    private string _lastKey = "";

    public FieldWatcher()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public event Action<FieldSnapshot>? SnapshotReady;

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "overlaylingo-uia"
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    private void Loop()
    {
        const int idleDelay = 300;
        const int activeDelay = 90;

        while (!_stop)
        {
            FieldSnapshot snapshot;
            try
            {
                snapshot = FieldReader.Read();
            }
            catch (Exception ex)
            {
                Log.Write("FieldReader.Read: " + ex.Message);
                snapshot = new FieldSnapshot();
            }

            Publish(snapshot);
            Thread.Sleep(snapshot.IsTextField ? activeDelay : idleDelay);
        }
    }

    private void Publish(FieldSnapshot snapshot)
    {
        if (_stop)
        {
            return;
        }

        // Не дёргаем UI одинаковыми снимками.
        string key = snapshot.Key;
        if (key == _lastKey)
        {
            return;
        }

        _lastKey = key;

        try
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    SnapshotReady?.Invoke(snapshot);
                }
                catch (Exception ex)
                {
                    Log.Write("SnapshotReady: " + ex.Message);
                }
            }));
        }
        catch
        {
            // Диспетчер уже остановлен — выходим тихо.
        }
    }

    public void Dispose()
    {
        _stop = true;
        _thread?.Join(500);
    }
}

internal static class Log
{
    private const long MaxBytes = 2_000_000;

    private static readonly object Gate = new();
    private static readonly string FilePath = System.IO.Path.Combine(AppContext.BaseDirectory, "overlaylingo.log");
    private static int _writes;

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                if (++_writes % 200 == 0)
                {
                    RotateIfNeeded();
                }

                File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            var info = new FileInfo(FilePath);
            if (info.Exists && info.Length > MaxBytes)
            {
                File.Move(FilePath, FilePath + ".old", true);
            }
        }
        catch
        {
        }
    }
}
