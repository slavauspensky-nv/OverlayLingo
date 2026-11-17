using System.Drawing;
using System.IO;
using System.Windows;

namespace WordPeek;

/// <summary>Снимок области экрана с переводом поверх исходного текста.</summary>
internal sealed class ScreenshotController
{
    private readonly Translator _translator;
    private readonly AppSettings _settings;
    private readonly Action<string> _notify;

    private bool _busy;
    private (string Text, bool ToEnglish, int Left, int Top, int Width, int Height)? _last;

    public ScreenshotController(Translator translator, AppSettings settings, Action<string> notify)
    {
        _translator = translator;
        _settings = settings;
        _notify = notify;
    }

    public void Trigger()
    {
        if (_busy)
        {
            _notify("Снимок уже делается");
            return;
        }

        _busy = true;

        try
        {
            if (!Native.GetCursorPos(out Native.POINT cursor))
            {
                _busy = false;
                return;
            }

            IntPtr monitor = Native.MonitorFromPoint(cursor, Native.MONITOR_DEFAULTTONEAREST);
            var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };

            if (monitor == IntPtr.Zero || !Native.GetMonitorInfo(monitor, ref info))
            {
                _busy = false;
                return;
            }

            double scale = Native.GetScaleAt(cursor.X, cursor.Y);
            bool selected = false;

            var selector = new RegionSelector(info.rcMonitor, scale);
            selector.AreaSelected += (left, top, width, height) =>
            {
                selected = true;
                _ = ProcessAsync(left, top, width, height);
            };

            selector.Closed += (_, _) =>
            {
                if (!selected)
                {
                    _busy = false;
                }
            };

            selector.Show();
            selector.Activate();
        }
        catch (Exception ex)
        {
            Log.Write("screenshot: " + ex.Message);
            _busy = false;
        }
    }

    private async Task ProcessAsync(int left, int top, int width, int height)
    {
        try
        {
            _notify("Распознаю текст…");

            // Даём окну выделения исчезнуть, иначе оно попадёт в снимок.
            await Task.Delay(220);

            (string Text, bool ToEnglish)? result = await Task.Run(async () =>
            {
                Bitmap shot = ScreenShotService.Capture(left, top, width, height);

                string mode = _settings.ScreenshotSource;
                string? forceLanguage = mode switch
                {
                    "en" => "en-US",
                    "ru" => "ru",
                    _ => null
                };

                OcrResult? ocr = ScreenShotService.Recognize(shot, forceLanguage);

                if (ocr is null || ocr.Lines.Count == 0)
                {
                    Log.Write("screenshot: текст не распознан");
                    shot.Dispose();
                    return ((string Text, bool ToEnglish)?)null;
                }

                // Направление берём из настройки, а в авторежиме — по кириллице.
                bool toEnglish = mode == "ru" || (mode != "en" && ocr.Cyrillic);
                string target = toEnglish ? "en" : "ru";

                string joined = ScreenShotService.BuildStructuredText(ocr.Lines);
                int lineBreaks = joined.Count(c => c == '\n');
                Log.Write($"screenshot: строк {ocr.Lines.Count}, кириллица={ocr.Cyrillic}, " +
                          $"направление={(toEnglish ? "RU→EN" : "EN→RU")}, переносов {lineBreaks}, символов {joined.Length}");

                if (joined.Length > 1500)
                {
                    joined = joined[..1500];
                }

                string? translation = await _translator.TranslateAsync(joined, CancellationToken.None, target);

                shot.Dispose();

                if (string.IsNullOrWhiteSpace(translation))
                {
                    return ((string Text, bool ToEnglish)?)null;
                }

                return (translation, toEnglish);
            });

            if (result is null)
            {
                _notify("Не удалось распознать текст");
                _busy = false;
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                _last = (result.Value.Text, result.Value.ToEnglish, left, top, width, height);

                var window = CreateWindow(result.Value.Text, result.Value.ToEnglish, left, top, width, height);
                window.Closed += (_, _) => _busy = false;
                window.Show();
            });
        }
        catch (Exception ex)
        {
            Log.Write("screenshot: " + ex);
            _busy = false;
        }
    }

    /// <summary>Создаёт окно перевода и подписывает его на клики по словам.</summary>
    private BubbleWindow CreateWindow(string text, bool toEnglish, int left, int top, int width, int height)
    {
        string backTarget = toEnglish ? "ru" : "en";

        var window = new BubbleWindow(text, _settings, left, top, left + width, top + height);

        window.WordClicked += (word, point) => _ = ShowWordTipAsync(window, word, point, backTarget);
        window.WordActivated += word => _ = SaveWordAsync(window, word, backTarget);

        return window;
    }

    /// <summary>Повторно показывает последний уже закрытый перевод.</summary>
    public void ReopenLast()
    {
        if (_last is null)
        {
            _notify("Пока нечего показывать: сделайте перевод по Ctrl+Alt+S");
            return;
        }

        var last = _last.Value;

        Application.Current.Dispatcher.Invoke(() =>
        {
            var window = CreateWindow(last.Text, last.ToEnglish, last.Left, last.Top, last.Width, last.Height);
            window.Show();
        });
    }

    /// <summary>Подсказка с обратным переводом слова над ним.</summary>
    private async Task ShowWordTipAsync(BubbleWindow window, string word, System.Windows.Point point, string target)
    {
        try
        {
            string? translation = await _translator.TranslateAsync(word, CancellationToken.None, target);
            Log.Write($"word: перевод \"{word}\" → {(translation is null ? "НЕТ" : translation)}");

            if (string.IsNullOrWhiteSpace(translation))
            {
                // Молча ничего не показывать нельзя — пользователь должен видеть, что произошло.
                Application.Current.Dispatcher.Invoke(() => window.ShowTip("перевод не удался", point));
                return;
            }

            Application.Current.Dispatcher.Invoke(() => window.ShowTip(translation, point));
        }
        catch (Exception ex)
        {
            Log.Write("screenshot tip: " + ex.Message);
        }
    }

    /// <summary>Двойной клик по слову: слово с переводом уходит в файл заметок.</summary>
    private async Task SaveWordAsync(BubbleWindow window, string word, string target)
    {
        try
        {
            string? translation = await _translator.TranslateAsync(word, CancellationToken.None, target);

            if (string.IsNullOrWhiteSpace(translation))
            {
                Application.Current.Dispatcher.Invoke(() => window.ShowStatus("Не удалось перевести слово"));
                return;
            }

            string path = NotePath();
            NoteWriter.Append(path, DateTime.Now, $"{word} — {translation}");

            Log.Write($"заметка: {word} — {translation} → {path}");

            Application.Current.Dispatcher.Invoke(() =>
                window.ShowStatus($"Сохранено в {Path.GetFileName(path)}"));
        }
        catch (Exception ex)
        {
            Log.Write("заметка: " + ex.Message);
            Application.Current.Dispatcher.Invoke(() => window.ShowStatus("Не удалось сохранить"));
        }
    }

    /// <summary>Файл заметок на рабочем столе; создастся при первой записи.</summary>
    private static string NotePath()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
        {
            desktop = AppContext.BaseDirectory;
        }

        return Path.Combine(desktop, "WordPeek.txt");
    }
}
