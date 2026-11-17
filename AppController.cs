using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace OverlayLingo;

internal sealed class AppController
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly OverlayWindow _overlay = new();
    private readonly TrayController _tray = new();
    private readonly HotkeyManager _hotkey = new();
    private readonly HotkeyManager _shotHotkey = new();
    private readonly HotkeyManager _reopenHotkey = new();
    private readonly FieldWatcher _watcher = new();
    private readonly KeyboardFallback _keyboard = new();
    private readonly Translator _translator;
    private ScreenshotController? _screenshot;
    private readonly DispatcherTimer _timer;

    private SettingsWindow? _settingsWindow;
    private TrainingWindow? _trainingWindow;
    private string _pendingText = "";
    private DateTime _pendingAt = DateTime.UtcNow;
    private string _lastRequested = "";
    private bool _inFlight;
    private bool _enabled;
    private int _anchorX;
    private int _anchorTop;
    private int _anchorBottom;
    private int _fieldLeft;
    private int _fieldTop;
    private int _fieldRight;
    private int _fieldBottom;
    private string _loggedText = "";
    private string _lastPhase = "";
    private DateTime _nextAttemptAt = DateTime.MinValue;
    private int _consecutiveFailures;
    private readonly Dictionary<string, DateTime> _blindLogged = new();
    private string _hookText = "";
    private bool _uiaOwnsField;
    private string _ownerProcess = "";
    private DateTime _suppressHookUntil = DateTime.MinValue;

    public AppController()
    {
        _translator = new Translator(_settings);
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _timer.Tick += OnTick;
    }

    public void Start()
    {
        _enabled = _settings.Enabled;

        _overlay.CopyRequested += OnCopyRequested;
        _overlay.ReplaceRequested += OnReplaceRequested;
        _watcher.SnapshotReady += OnSnapshot;
        _keyboard.TextChanged += OnKeyboardText;
        _tray.ToggleRequested += Toggle;
        _tray.TrainingRequested += OpenTraining;
        _tray.SettingsRequested += OpenSettings;
        _tray.DiagnosticsRequested += RunDiagnostics;
        _tray.AboutRequested += ShowAbout;
        _tray.ExitRequested += Shutdown;
        _hotkey.Pressed += Toggle;

        _screenshot = new ScreenshotController(_translator, _settings, message => _tray.Notify("OverlayLingo", message));
        _shotHotkey.Pressed += () => _screenshot.Trigger();
        if (!_shotHotkey.TryRegister(Native.MOD_CONTROL | Native.MOD_ALT, 0x53))
        {
            _tray.Notify("OverlayLingo", "Не удалось занять Ctrl+Alt+S для снимка с переводом.");
        }

        _reopenHotkey.Pressed += () => _screenshot.ReopenLast();
        if (!_reopenHotkey.TryRegister(_settings.ReopenHotkeyModifiers, _settings.ReopenHotkeyKey))
        {
            _tray.Notify("OverlayLingo", "Не удалось занять комбинацию повторного показа перевода.");
        }

        _tray.SetEnabled(_enabled);

        if (_settings.BrowserAccessibility)
        {
            AccessibilityMode.TryEnable();
        }

        if (!_hotkey.TryRegister(_settings.HotkeyModifiers, _settings.HotkeyKey))
        {
            _tray.Notify("OverlayLingo",
                "Не удалось занять горячую клавишу. Выберите другую в настройках.");
        }

        _watcher.Start();
        if (_settings.KeyboardFallback)
        {
            _keyboard.Start();
        }

        _timer.Start();

        try
        {
            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            Log.Write("build " + File.GetLastWriteTime(exe).ToString("yyyy-MM-dd HH:mm:ss"));
        }
        catch
        {
        }

        _tray.Notify("OverlayLingo запущен", _enabled
            ? "Печатайте в любом поле — перевод появится рядом. Ctrl+Alt+T — включить или выключить."
            : "Внимание: перевод выключен. Нажмите Ctrl+Alt+T, чтобы включить.");

        Log.Write("started");
    }

    private void Toggle()
    {
        _enabled = !_enabled;
        _settings.Enabled = _enabled;
        _settings.Save();
        _tray.SetEnabled(_enabled);

        _tray.Notify("OverlayLingo", _enabled
            ? "Перевод включён"
            : "Перевод выключен. Нажмите ту же комбинацию, чтобы вернуть.");

        if (!_enabled)
        {
            _overlay.HideTranslation();
            _pendingText = "";
        }
    }

    private void OnSnapshot(FieldSnapshot snapshot)
    {
        string phase = $"{snapshot.ProcessName}|{snapshot.IsTextField}|{snapshot.ControlTypeName}|{snapshot.HasPosition}";
        if (phase != _lastPhase)
        {
            _lastPhase = phase;
            string debugText = snapshot.Text.Length > 40 ? snapshot.Text[..40] + "…" : snapshot.Text;
            Log.Write($"snap: field={snapshot.IsTextField} pos={snapshot.HasPosition} " +
                      $"proc={snapshot.ProcessName} type={snapshot.ControlTypeName} text=\"{debugText}\"");
        }

        if (!_enabled)
        {
            return;
        }

        bool fieldWithText = snapshot.IsTextField && !snapshot.IsPassword && snapshot.HasPosition;
        _uiaOwnsField = fieldWithText;

        if (!fieldWithText)
        {
            if (!snapshot.IsTextField)
            {
                LogBlindWindow(snapshot);
            }

            // Если UIA поле не видит, но человек что-то печатает — плашку ведёт перехват клавиатуры.
            if (string.IsNullOrEmpty(_hookText))
            {
                _overlay.HideTranslation();
                ResetPending();
            }

            return;
        }

        string text = snapshot.Text.Trim();
        if (text.Length < 2)
        {
            _overlay.HideTranslation();
            ResetPending();
            return;
        }

        // UIA — основной источник, буфер перехвата больше не актуален.
        _hookText = "";
        _ownerProcess = snapshot.ProcessName;

        if (snapshot.HasCaret)
        {
            _anchorX = snapshot.CaretX;
            _anchorTop = snapshot.CaretY;
            _anchorBottom = snapshot.CaretY + snapshot.CaretH;
        }
        else
        {
            _anchorX = snapshot.AnchorX;
            _anchorTop = snapshot.AnchorTop;
            _anchorBottom = snapshot.AnchorY;
        }

        _fieldLeft = snapshot.FieldLeft;
        _fieldTop = snapshot.FieldTop;
        _fieldRight = snapshot.FieldRight;
        _fieldBottom = snapshot.FieldBottom;

        if (text != _loggedText)
        {
            _loggedText = text;
            string sample = text.Length > 90 ? text[..90] + "…" : text;
            Log.Write($"field proc={snapshot.ProcessName} type={snapshot.ControlTypeName} " +
                      $"caret={snapshot.HasCaret}({_anchorX},{_anchorTop},{_anchorBottom}) text=\"{sample}\"");
        }

        if (_overlay.IsVisible)
        {
            _overlay.Reposition(_anchorX, _anchorBottom, _anchorTop);
        }

        if (text != _pendingText)
        {
            _pendingText = text;
            _pendingAt = DateTime.UtcNow;
        }
    }

    private void ResetPending()
    {
        _pendingText = "";
        _lastRequested = "";
    }

    /// <summary>Текст из пассивного перехвата клавиатуры — для приложений, скрытых от UIA.</summary>
    private void OnKeyboardText(string text, int cursorX, int cursorY)
    {
        // Хук приходит из своего потока — переходим в UI-поток, иначе трогать окно нельзя.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => OnKeyboardText(text, cursorX, cursorY)));
            return;
        }

        if (!_enabled || _uiaOwnsField || DateTime.UtcNow < _suppressHookUntil)
        {
            return;
        }

        if (text != _hookText)
        {
            string sample = text.Length > 60 ? text[..60] + "…" : text;
            Log.Write($"hook: \"{sample}\"");
        }

        _hookText = text;
        _ownerProcess = ForegroundProcessName();

        string trimmed = text.Trim();
        if (trimmed.Length < 2)
        {
            _overlay.HideTranslation();
            ResetPending();
            return;
        }

        _anchorX = cursorX;
        _anchorTop = cursorY - 18;
        _anchorBottom = cursorY;

        if (trimmed != _pendingText)
        {
            _pendingText = trimmed;
            _pendingAt = DateTime.UtcNow;
        }
    }

    /// <summary>Пишет в лог, что приложение не отдаёт текст поля, не чаще раза в 30 секунд.</summary>
    private void LogBlindWindow(FieldSnapshot snapshot)
    {
        if (string.IsNullOrEmpty(snapshot.ProcessName))
        {
            return;
        }

        if (_blindLogged.TryGetValue(snapshot.ProcessName, out DateTime last) &&
            (DateTime.UtcNow - last).TotalSeconds < 30)
        {
            return;
        }

        _blindLogged[snapshot.ProcessName] = DateTime.UtcNow;
        Log.Write($"blind: proc={snapshot.ProcessName} type={snapshot.ControlTypeName}");
    }

    /// <summary>
    /// На коротком тексте работаем по символу, на длинном — придерживаем отправку,
    /// иначе пересчёт всего абзаца на каждую букву съедает лимит запросов.
    /// </summary>
    private int EffectiveDebounce(string text)
    {
        return text.Length > 300 ? Math.Max(_settings.DebounceMs, 350) : _settings.DebounceMs;
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (!_enabled || _inFlight)
        {
            return;
        }

        string text = _pendingText;
        if (text.Length < 2 || text == _lastRequested)
        {
            return;
        }

        if (DateTime.UtcNow < _nextAttemptAt)
        {
            return;
        }

        if ((DateTime.UtcNow - _pendingAt).TotalMilliseconds < EffectiveDebounce(text))
        {
            return;
        }

        _lastRequested = text;
        _inFlight = true;

        try
        {
            string? translation = await _translator.TranslateAsync(text, CancellationToken.None);

            if (string.IsNullOrEmpty(translation))
            {
                _consecutiveFailures++;
                _lastRequested = "";
                int delaySeconds = Math.Min(15, 2 * _consecutiveFailures);
                _nextAttemptAt = DateTime.UtcNow.AddSeconds(delaySeconds);

                if (_consecutiveFailures == 3)
                {
                    _tray.Notify("OverlayLingo",
                        "Перевод не приходит. Проверьте интернет или провайдера в настройках.");
                }

                return;
            }

            _consecutiveFailures = 0;

            string shown = translation.Length > 90 ? translation[..90] + "…" : translation;
            Log.Write($"ok: {shown}");

            if (text == _pendingText && _enabled)
            {
                _overlay.ShowTranslation(translation, _anchorX, _anchorBottom, _anchorTop);
            }
        }
        catch (Exception ex)
        {
            Log.Write("tick: " + ex.Message);
        }
        finally
        {
            _inFlight = false;
        }
    }

    private void OnCopyRequested(string translation)
    {
        if (string.IsNullOrEmpty(translation))
        {
            return;
        }

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(translation);
                _tray.Notify("OverlayLingo", "Перевод скопирован");
                return;
            }
            catch (Exception ex)
            {
                Log.Write("clipboard: " + ex.Message);
                Thread.Sleep(60);
            }
        }
    }

    /// <summary>Правый клик по плашке: заменить текст в поле переводом.</summary>
    private void OnReplaceRequested(string translation)
    {
        if (string.IsNullOrWhiteSpace(translation))
        {
            return;
        }

        string foreground = ForegroundProcessName();
        if (!string.IsNullOrEmpty(_ownerProcess) &&
            !string.Equals(foreground, _ownerProcess, StringComparison.OrdinalIgnoreCase))
        {
            Log.Write($"replace отменена: было {_ownerProcess}, сейчас {foreground}");
            _tray.Notify("OverlayLingo", "Фокус уже в другом окне — замена отменена");
            return;
        }

        try
        {
            ApplyReplacement(translation, usePaste: true);
            Log.Write($"replaced: \"{translation.Trim()}\"");
            _tray.Notify("OverlayLingo", "Текст заменён переводом");

            VerifyReplace(translation, allowRetry: true);
        }
        catch (Exception ex)
        {
            Log.Write("replace: " + ex.Message);
            _tray.Notify("OverlayLingo", "Не удалось заменить текст");
        }
    }

    private void ApplyReplacement(string translation, bool usePaste)
    {
        // Ввод, который мы сами синтезируем, не должен попадать в буфер перехвата.
        _suppressHookUntil = DateTime.UtcNow.AddMilliseconds(1400);

        // Плашка уже скрыта — даём системе вернуть фокус окну с текстом.
        Thread.Sleep(120);

        // Кликаем внутрь поля: окно может быть активным, а сам контрол — без фокуса.
        if (_fieldRight > _fieldLeft + 8 && _fieldBottom > _fieldTop + 8)
        {
            int clickX = _fieldLeft + Math.Min(24, (_fieldRight - _fieldLeft) / 3);
            int clickY = _fieldTop + Math.Min(18, (_fieldBottom - _fieldTop) / 3);
            InputSimulator.ClickAt(clickX, clickY);
            Thread.Sleep(140);
            Log.Write($"replace click: ({clickX},{clickY}) в поле ({_fieldLeft},{_fieldTop})-({_fieldRight},{_fieldBottom})");
        }

        IntPtr foreground = Native.GetForegroundWindow();

        // Клик внутрь поля ставит фокус именно текстовому контролу.
        // Никакого SetFocus после него делать нельзя: он снимает фокус с поля
        // и возвращает его окну, после чего Ctrl+A и Ctrl+V уходят в пустоту.
        if (_fieldRight > _fieldLeft + 8 && _fieldBottom > _fieldTop + 8)
        {
            int clickX = _fieldLeft + Math.Min(24, (_fieldRight - _fieldLeft) / 3);
            int clickY = _fieldTop + Math.Min(18, (_fieldBottom - _fieldTop) / 3);
            InputSimulator.ClickAt(clickX, clickY);
            Thread.Sleep(160);
            Log.Write($"replace click: ({clickX},{clickY}) в поле ({_fieldLeft},{_fieldTop})-({_fieldRight},{_fieldBottom})");
        }
        else
        {
            Native.SetForegroundWindow(foreground);
            Thread.Sleep(120);
        }

        string focusProcess = FocusProcessName();
        Log.Write($"replace focus: окно={foreground}, процесс фокуса=\"{focusProcess}\"");

        if (!string.IsNullOrEmpty(_ownerProcess) &&
            !string.Equals(focusProcess, _ownerProcess, StringComparison.OrdinalIgnoreCase))
        {
            // Ввод уйдёт не туда — это опаснее, чем не заменить текст.
            Log.Write("replace: фокус не в целевом окне, ввод не отправляю");
            return;
        }

        InputSimulator.SelectAll();
        Thread.Sleep(80);

        if (usePaste && TrySetClipboard(translation))
        {
            InputSimulator.Paste();
        }
        else
        {
            InputSimulator.TypeUnicode(translation);
        }

        Thread.Sleep(160);
        _keyboard.ResetText();
        _hookText = "";

        // Поле теперь содержит ровно перевод: не переводим его обратно.
        string trimmed = translation.Trim();
        _pendingText = trimmed;
        _lastRequested = trimmed;
        _pendingAt = DateTime.UtcNow;
    }

    private static bool TrySetClipboard(string text)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);

                // Запечатываем данные в буфере: иначе приложение-получатель просит их
                // у нас, а наш UI-поток в этот момент занят паузами и не отвечает.
                Clipboard.Flush();
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("replace clipboard: " + ex.Message);
                Thread.Sleep(70);
            }
        }

        return false;
    }

    /// <summary>Проверяет результат замены и при неудаче пробует другой способ.</summary>
    private void VerifyReplace(string expected, bool allowRetry)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(450);

            bool ok;
            string actual;

            try
            {
                FieldSnapshot snapshot = FieldReader.Read();
                actual = snapshot.Text.Trim();
                ok = string.Equals(actual, expected.Trim(), StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                Log.Write("replace check: " + ex.Message);
                return;
            }

            if (ok)
            {
                Log.Write("replace check: успешно");
                return;
            }

            Log.Write($"replace check: НЕ СРАБОТАЛО поле=\"{Trim(actual, 60)}\"");

            if (!allowRetry)
            {
                // Оба способа не сработали — хотя бы оставляем перевод в буфере.
                try
                {
                    _ = Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                    {
                        if (TrySetClipboard(expected))
                        {
                            _tray.Notify("OverlayLingo",
                                "Заменить не вышло. Перевод скопирован — вставьте вручную (Ctrl+V).");
                        }
                    }));
                }
                catch
                {
                }

                return;
            }

            try
            {
                _ = Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    ApplyReplacement(expected, usePaste: false);
                    Log.Write("replaced: повтор посимвольным вводом");
                    VerifyReplace(expected, allowRetry: false);
                }));
            }
            catch (Exception ex)
            {
                Log.Write("replace retry: " + ex.Message);
            }
        });
    }

    /// <summary>Имя процесса, которому сейчас принадлежит клавиатурный фокус.</summary>
    private static string FocusProcessName()
    {
        try
        {
            IntPtr foreground = Native.GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return "";
            }

            uint threadId = Native.GetWindowThreadProcessId(foreground, out _);
            var info = new Native.GUITHREADINFO
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.GUITHREADINFO>()
            };

            if (!Native.GetGUIThreadInfo(threadId, ref info) || info.hwndFocus == IntPtr.Zero)
            {
                return "";
            }

            Native.GetWindowThreadProcessId(info.hwndFocus, out uint processId);
            using var proc = System.Diagnostics.Process.GetProcessById((int)processId);
            return proc.ProcessName;
        }
        catch
        {
            return "";
        }
    }

    private static string Trim(string text, int limit)
    {
        return text.Length > limit ? text[..limit] + "…" : text;
    }

    private static string ForegroundProcessName()
    {
        IntPtr hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return "";
        }

        Native.GetWindowThreadProcessId(hwnd, out uint processId);
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById((int)processId);
            return proc.ProcessName;
        }
        catch
        {
            return "";
        }
    }

    private void OpenTraining()
    {
        if (_trainingWindow is { IsVisible: true })
        {
            _trainingWindow.Activate();
            return;
        }

        _trainingWindow = new TrainingWindow();
        _trainingWindow.Closed += (_, _) => _trainingWindow = null;
        _trainingWindow.Show();
        _trainingWindow.Activate();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings);
        _settingsWindow.Saved += OnSettingsSaved;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnSettingsSaved()
    {
        _enabled = _settings.Enabled;
        _tray.SetEnabled(_enabled);

        if (_settings.KeyboardFallback)
        {
            _keyboard.Start();
        }
        else
        {
            _keyboard.Dispose();
            _hookText = "";
        }

        if (!_hotkey.TryRegister(_settings.HotkeyModifiers, _settings.HotkeyKey))
        {
            _tray.Notify("OverlayLingo",
                "Эта горячая клавиша занята другой программой. Попробуйте другую.");
        }

        if (!_reopenHotkey.TryRegister(_settings.ReopenHotkeyModifiers, _settings.ReopenHotkeyKey))
        {
            _tray.Notify("OverlayLingo", "Комбинация повторного показа перевода занята другой программой.");
        }

        if (!_enabled)
        {
            _overlay.HideTranslation();
            _pendingText = "";
        }
    }

    private AboutWindow? _aboutWindow;

    private void ShowAbout()
    {
        if (_aboutWindow is { IsVisible: true })
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new AboutWindow();
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Show();
        _aboutWindow.Activate();
    }

    private void RunDiagnostics()
    {
        var report = new System.Text.StringBuilder();

        try
        {
            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            report.AppendLine($"build: {File.GetLastWriteTime(exe):yyyy-MM-dd HH:mm:ss}");
            report.AppendLine($"settings: enabled={_settings.Enabled} provider={_settings.Provider} " +
                              $"debounce={_settings.DebounceMs} accessibility={_settings.BrowserAccessibility} " +
                              $"hook={_settings.KeyboardFallback} startWithWindows={_settings.StartWithWindows}");
            report.AppendLine($"screen reader flag: {AccessibilityMode.IsCurrentlyEnabled()}");
            report.AppendLine($"uia owns field: {_uiaOwnsField}, hook text length: {_hookText.Length}");
            report.AppendLine("--- дерево активного окна ---");
            report.AppendLine(TreeDump.BuildReport(null, 4));

            string path = Path.Combine(AppContext.BaseDirectory, "diagnostics.log");
            File.WriteAllText(path, report.ToString());
            _tray.Notify("OverlayLingo", "Диагностика сохранена: diagnostics.log");
        }
        catch (Exception ex)
        {
            Log.Write("diagnostics: " + ex.Message);
            _tray.Notify("OverlayLingo", "Не удалось сохранить диагностику");
        }
    }

    private void Shutdown()
    {
        try
        {
            _timer.Stop();
            _watcher.Dispose();
            _keyboard.Dispose();
            _hotkey.Dispose();
            _shotHotkey.Dispose();
            _reopenHotkey.Dispose();
            AccessibilityMode.Restore();
            _tray.Dispose();
        }
        catch (Exception ex)
        {
            Log.Write("shutdown: " + ex.Message);
        }

        Application.Current.Shutdown();
    }
}
