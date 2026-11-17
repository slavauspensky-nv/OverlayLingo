using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace OverlayLingo;

internal sealed class FieldSnapshot
{
    public bool IsTextField;
    public bool IsPassword;
    public bool HasCaret;
    public int CaretX;
    public int CaretY;
    public int CaretH;
    public int AnchorX;
    public int AnchorY;
    public int AnchorTop;

    // Прямоугольник самого поля ввода — нужен, чтобы попасть в него кликом.
    public int FieldLeft;
    public int FieldTop;
    public int FieldRight;
    public int FieldBottom;
    public string Text = "";
    public string ControlTypeName = "";
    public string ProcessName = "";
    public int ProcessId;

    public string Key =>
        $"{IsTextField}|{IsPassword}|{HasCaret}|{CaretX}|{CaretY}|{CaretH}|{AnchorX}|{AnchorY}|" +
        $"{ProcessName}|{ControlTypeName}|{Text}";

    public bool HasPosition => HasCaret || AnchorY > 0;
}

/// <summary>
/// Считывает активное поле ввода через UI Automation.
/// Работает синхронно, вызывается только из фонового MTA-потока.
/// </summary>
internal static class FieldReader
{
    private const int TextSampleLimit = 600;

    /// <summary>
    /// Процессы, у которых содержимое поля доступно только через имя элемента.
    /// Win11 Блокнот — типичный пример: ControlType.Pane, текстовых паттернов нет.
    /// </summary>
    private static readonly HashSet<string> KnownNameOnlyProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "notepad"
    };

    /// <summary>
    /// У этих процессов ControlType.Document — это реальный документ, который можно переводить.
    /// У браузеров и Electron «документ» — вся страница целиком, её переводить нельзя.
    /// </summary>
    private static readonly HashSet<string> DocumentEditors = new(StringComparer.OrdinalIgnoreCase)
    {
        "winword", "wordpad", "write"
    };

    public static FieldSnapshot Read()
    {
        var snapshot = new FieldSnapshot();

        IntPtr foreground = Native.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return snapshot;
        }

        Native.GetWindowThreadProcessId(foreground, out uint foregroundPid);
        snapshot.ProcessId = (int)foregroundPid;
        snapshot.ProcessName = ProcessNameOf((int)foregroundPid);

        // Быстрый путь: UIA сам знает фокус и он совпадает с активным окном.
        AutomationElement? focused = TryGetFocused();
        if (focused is not null && ProcessIdOf(focused) == (int)foregroundPid && TryFillFrom(focused, snapshot))
        {
            return snapshot;
        }

        // Надёжный путь: активное окно — источник истины. UIA иногда «залипает»
        // на Chromium-окне и продолжает отдавать его как сфокусированное.
        FieldSnapshot fromWindow = ReadFromHandle(foreground);
        if (fromWindow.IsTextField)
        {
            return fromWindow;
        }

        // Поле не нашли — отдаём хотя бы данные активного окна, чтобы работала диагностика.
        if (fromWindow.ControlTypeName.Length > 0)
        {
            snapshot.ControlTypeName = fromWindow.ControlTypeName;
        }

        return snapshot;
    }

    private static string ProcessNameOf(int processId)
    {
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(processId);
            return proc.ProcessName;
        }
        catch
        {
            return "";
        }
    }

    /// <summary>То же чтение, но для конкретного окна — нужно для диагностики.</summary>
    public static FieldSnapshot ReadFromHandle(IntPtr handle, bool allowOwnProcess = false)
    {
        var snapshot = new FieldSnapshot();

        AutomationElement? window = null;
        try
        {
            window = AutomationElement.FromHandle(handle);
        }
        catch
        {
        }

        if (window is null)
        {
            return snapshot;
        }

        try
        {
            snapshot.ControlTypeName = window.Current.ControlType.ProgrammaticName;
            snapshot.ProcessId = window.Current.ProcessId;
            if (snapshot.ProcessName.Length == 0)
            {
                snapshot.ProcessName = ProcessNameOf(snapshot.ProcessId);
            }
        }
        catch
        {
        }

        if (TryFillFrom(window, snapshot, allowOwnProcess))
        {
            return snapshot;
        }

        AutomationElement? inside = FindFocusedDescendant(window);
        if (inside is not null)
        {
            var retry = new FieldSnapshot();
            if (TryFillFrom(inside, retry, allowOwnProcess))
            {
                return retry;
            }
        }

        return snapshot;
    }

    private static int ProcessIdOf(AutomationElement element)
    {
        try
        {
            return element.Current.ProcessId;
        }
        catch
        {
            return 0;
        }
    }

    private static AutomationElement? TryGetFocused()
    {
        try
        {
            return AutomationElement.FocusedElement;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryFillFrom(AutomationElement element, FieldSnapshot snapshot, bool allowOwnProcess = false)
    {
        int pid = 0;
        try
        {
            pid = element.Current.ProcessId;
        }
        catch
        {
        }

        // Своё же окно настроек не переводим.
        if (!allowOwnProcess && pid != 0 && pid == Environment.ProcessId)
        {
            return false;
        }

        snapshot.ProcessId = pid;
        try
        {
            using var proc = Process.GetProcessById(pid);
            snapshot.ProcessName = proc.ProcessName;
        }
        catch
        {
        }

        ControlType? controlType = null;
        try
        {
            controlType = element.Current.ControlType;
        }
        catch
        {
        }

        snapshot.ControlTypeName = controlType?.ProgrammaticName ?? "?";

        bool isPassword = false;
        try
        {
            isPassword = element.Current.IsPassword;
        }
        catch
        {
        }

        if (isPassword)
        {
            snapshot.IsTextField = true;
            snapshot.IsPassword = true;
            return true;
        }

        bool isEdit = controlType == ControlType.Edit;
        bool isDocument = controlType == ControlType.Document;

        var textPattern = TryTextPattern(element);
        var valuePattern = TryValuePattern(element);
        if (textPattern is null && valuePattern is null)
        {
            // Некоторые современные поля (Win11 Блокнот) не отдают Text/Value:
            // единственный доступный текст — имя элемента, равное содержимому.
            // Ограничиваем это известными процессами, иначе в перевод попадают
            // подписи кнопок проводника и прочий интерфейс.
            return KnownNameOnlyProcesses.Contains(snapshot.ProcessName)
                   && TryUseAccessibleName(element, snapshot);
        }

        // Не всякое поле — Edit или Document: Win11 Блокнот, например, отдаёт Pane.
        // Поэтому решаем по наличию текстового паттерна, а не по типу контрола.
        if (isDocument && !DocumentEditors.Contains(snapshot.ProcessName))
        {
            // У браузеров Document — это вся страница, а не поле ввода.
            return false;
        }

        if (!isEdit && !isDocument && !IsEditable(textValuePattern: valuePattern, textPattern: textPattern))
        {
            return false;
        }

        snapshot.IsTextField = true;
        snapshot.Text = ReadText(textPattern, valuePattern);

        if (textPattern is not null && TryCaretFromTextPattern(textPattern, out var caret))
        {
            snapshot.HasCaret = true;
            snapshot.CaretX = (int)caret.X;
            snapshot.CaretY = (int)caret.Y;
            snapshot.CaretH = Math.Max((int)caret.Height, 16);
            snapshot.AnchorTop = (int)caret.Y;
            snapshot.AnchorY = (int)(caret.Y + Math.Max(caret.Height, 16));
        }
        else if (TryCaretFromForeground(out var fallback))
        {
            snapshot.HasCaret = true;
            snapshot.CaretX = (int)fallback.X;
            snapshot.CaretY = (int)fallback.Y;
            snapshot.CaretH = Math.Max((int)fallback.Height, 16);
        }

        try
        {
            var box = element.Current.BoundingRectangle;
            if (!box.IsEmpty && box.Width > 4 && box.Height > 4)
            {
                snapshot.FieldLeft = (int)box.X;
                snapshot.FieldTop = (int)box.Y;
                snapshot.FieldRight = (int)box.Right;
                snapshot.FieldBottom = (int)box.Bottom;

                if (box.Height < 220)
                {
                    snapshot.AnchorX = (int)box.X;
                    snapshot.AnchorY = (int)box.Bottom;
                }
            }
        }
        catch
        {
        }

        return true;
    }

    private static bool TryUseAccessibleName(AutomationElement element, FieldSnapshot snapshot)
    {
        try
        {
            if (!element.Current.HasKeyboardFocus)
            {
                return false;
            }

            ControlType? type = element.Current.ControlType;
            if (type != ControlType.Pane && type != ControlType.Edit && type != ControlType.Document)
            {
                return false;
            }

            string name = element.Current.Name;
            if (string.IsNullOrWhiteSpace(name) || name.Length < 2)
            {
                return false;
            }

            snapshot.IsTextField = true;
            snapshot.Text = name;

            var box = element.Current.BoundingRectangle;
            if (box.IsEmpty || box.Width <= 4 || box.Height <= 4)
            {
                return true;
            }

            snapshot.FieldLeft = (int)box.X;
            snapshot.FieldTop = (int)box.Y;
            snapshot.FieldRight = (int)box.Right;
            snapshot.FieldBottom = (int)box.Bottom;

            // Каретку такое поле не отдаёт. Считаем, что печатают в конце текста:
            // строка с кареткой последняя, плашку ставим сразу под ней.
            double scale = Native.GetScaleAt((int)box.X, (int)box.Y);
            int lineHeight = Math.Max((int)Math.Round(24 * scale), 12);
            int topPadding = Math.Max((int)Math.Round(10 * scale), 4);

            int lineCount = 1;
            foreach (char symbol in name)
            {
                if (symbol == '\n')
                {
                    lineCount++;
                }
            }

            lineCount = Math.Clamp(lineCount, 1, 80);

            int lineTop = (int)box.Y + topPadding + (lineCount - 1) * lineHeight;
            int lineBottom = lineTop + lineHeight;

            int limit = (int)box.Bottom - 2;
            if (lineBottom > limit)
            {
                lineBottom = limit;
                lineTop = Math.Max(lineBottom - lineHeight, (int)box.Y);
            }

            snapshot.AnchorX = (int)box.X + (int)Math.Round(6 * scale);
            snapshot.AnchorTop = lineTop;
            snapshot.AnchorY = lineBottom;

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Для нестандартных контейнеров: поле, если оно редактируемое или отдаёт выделение.</summary>
    private static bool IsEditable(ValuePattern? textValuePattern, TextPattern? textPattern)
    {
        try
        {
            if (textValuePattern is not null && !textValuePattern.Current.IsReadOnly)
            {
                return true;
            }
        }
        catch
        {
        }

        try
        {
            if (textPattern is not null && textPattern.GetSelection().Length > 0)
            {
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    /// <summary>
    /// Ограниченный обход дерева в поисках элемента с клавиатурным фокусом.
    /// Ограничение по числу узлов нужно, чтобы не зависнуть на огромных документах.
    /// </summary>
    private static AutomationElement? FindFocusedDescendant(AutomationElement root)
    {
        const int maxNodes = 200;

        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var queue = new Queue<(AutomationElement Element, int Depth)>();
            queue.Enqueue((root, 0));
            int visited = 0;

            while (queue.Count > 0 && visited < maxNodes)
            {
                var (element, depth) = queue.Dequeue();
                visited++;

                if (depth > 0)
                {
                    bool hasFocus = false;
                    try
                    {
                        hasFocus = element.Current.HasKeyboardFocus;
                    }
                    catch
                    {
                    }

                    if (hasFocus)
                    {
                        return element;
                    }
                }

                if (depth >= 8)
                {
                    continue;
                }

                AutomationElement? child;
                try
                {
                    child = walker.GetFirstChild(element);
                }
                catch
                {
                    continue;
                }

                while (child is not null)
                {
                    queue.Enqueue((child, depth + 1));
                    try
                    {
                        child = walker.GetNextSibling(child);
                    }
                    catch
                    {
                        break;
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static string ReadText(TextPattern? textPattern, ValuePattern? valuePattern)
    {
        string text = "";

        if (textPattern is not null)
        {
            try
            {
                string sample = textPattern.DocumentRange.GetText(TextSampleLimit);
                if (sample.Length < TextSampleLimit)
                {
                    text = sample;
                }
                else
                {
                    // Документ большой (Word, страница в браузере) — берём абзац у каретки.
                    var selection = textPattern.GetSelection();
                    if (selection.Length > 0)
                    {
                        var paragraph = selection[0].Clone();
                        paragraph.ExpandToEnclosingUnit(TextUnit.Paragraph);
                        text = paragraph.GetText(-1);
                    }
                    else
                    {
                        text = sample;
                    }
                }
            }
            catch
            {
            }
        }

        if (string.IsNullOrEmpty(text) && valuePattern is not null)
        {
            try
            {
                text = valuePattern.Current.Value ?? "";
            }
            catch
            {
            }
        }

        return text;
    }

    private static TextPattern? TryTextPattern(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object pattern))
            {
                return pattern as TextPattern;
            }
        }
        catch
        {
        }

        return null;
    }

    private static ValuePattern? TryValuePattern(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern))
            {
                return pattern as ValuePattern;
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool TryCaretFromTextPattern(TextPattern textPattern, out Rect rect)
    {
        rect = Rect.Empty;

        try
        {
            var selection = textPattern.GetSelection();
            if (selection.Length == 0)
            {
                return false;
            }

            var range = selection[0];
            if (TryPickRect(range.GetBoundingRectangles(), out rect))
            {
                return true;
            }

            // Свёрнутая каретка часто отдаёт пустой прямоугольник — растягиваем на символ вперёд.
            var forward = range.Clone();
            forward.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 1);
            if (TryPickRect(forward.GetBoundingRectangles(), out rect))
            {
                return true;
            }

            // Конец текста — тянем назад и берём правый край предыдущего символа.
            var backward = range.Clone();
            backward.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -1);
            if (TryPickRect(backward.GetBoundingRectangles(), out var previous))
            {
                rect = new Rect(previous.X + previous.Width, previous.Y, 2, previous.Height);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TryPickRect(Rect[] rects, out Rect rect)
    {
        rect = Rect.Empty;

        if (rects is null)
        {
            return false;
        }

        foreach (Rect candidate in rects)
        {
            if (candidate.Height <= 0)
            {
                continue;
            }

            rect = new Rect(candidate.X, candidate.Y, Math.Max(candidate.Width, 2), candidate.Height);
            return true;
        }

        return false;
    }

    private static bool TryCaretFromForeground(out Rect rect)
    {
        rect = Rect.Empty;

        try
        {
            IntPtr foreground = Native.GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return false;
            }

            uint threadId = Native.GetWindowThreadProcessId(foreground, out _);
            var info = new Native.GUITHREADINFO
            {
                cbSize = Marshal.SizeOf<Native.GUITHREADINFO>()
            };

            if (!Native.GetGUIThreadInfo(threadId, ref info) || info.hwndCaret == IntPtr.Zero)
            {
                return false;
            }

            var point = new Native.POINT { X = info.rcCaret.Left, Y = info.rcCaret.Top };
            if (!Native.ClientToScreen(info.hwndCaret, ref point))
            {
                return false;
            }

            int width = Math.Max(info.rcCaret.Right - info.rcCaret.Left, 2);
            int height = Math.Max(info.rcCaret.Bottom - info.rcCaret.Top, 16);
            rect = new Rect(point.X, point.Y, width, height);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
