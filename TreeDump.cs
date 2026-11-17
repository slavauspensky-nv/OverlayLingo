using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace WordPeek;

/// <summary>
/// Выгружает дерево UI Automation активного окна (или окна указанного процесса).
/// Нужно, чтобы понять, отдаёт ли приложение (Chrome, Electron) свои элементы вообще.
/// </summary>
internal static class TreeDump
{
    private const int MaxDepth = 5;
    private const int MaxNodes = 300;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    public static void Run(string? maxDepthArgument, string? processArgument)
    {
        int maxDepth = MaxDepth;
        if (int.TryParse(maxDepthArgument, out int parsed))
        {
            maxDepth = Math.Clamp(parsed, 1, 8);
        }

        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "tree.log"),
            BuildReport(processArgument, maxDepth));
    }

    /// <summary>Текстовый отчёт о дереве доступности активного окна или окна указанного процесса.</summary>
    public static string BuildReport(string? processArgument, int maxDepth = MaxDepth)
    {
        var log = new StringBuilder();

        try
        {
            IntPtr target = string.IsNullOrWhiteSpace(processArgument)
                ? Native.GetForegroundWindow()
                : FindWindowOfProcess(processArgument);

            if (target == IntPtr.Zero)
            {
                log.AppendLine($"окно процесса \"{processArgument}\" не найдено");
                return log.ToString();
            }

            var root = AutomationElement.FromHandle(target);

            string proc = "?";
            try
            {
                proc = Process.GetProcessById(root.Current.ProcessId).ProcessName;
            }
            catch
            {
            }

            log.AppendLine($"hwnd={target} title=\"{Title(target)}\" proc={proc} " +
                           $"type={root.Current.ControlType.ProgrammaticName} name=\"{root.Current.Name}\"");

            int count = 0;
            Walk(root, 0, maxDepth, log, ref count);
            log.AppendLine($"nodes={count}");
        }
        catch (Exception ex)
        {
            log.AppendLine("ERR " + ex);
        }

        return log.ToString();
    }

    internal static IntPtr FindWindowOfProcess(string processName)
    {
        IntPtr found = IntPtr.Zero;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindowTextLength(hwnd) == 0)
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out uint pid);
            try
            {
                string name = Process.GetProcessById((int)pid).ProcessName;
                if (string.Equals(name, processName, StringComparison.OrdinalIgnoreCase))
                {
                    found = hwnd;
                    return false;
                }
            }
            catch
            {
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    private static string Title(IntPtr hwnd)
    {
        var buffer = new StringBuilder(512);
        GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static void Walk(AutomationElement element, int depth, int maxDepth, StringBuilder log, ref int count)
    {
        if (depth >= maxDepth || count >= MaxNodes)
        {
            return;
        }

        AutomationElement? child;
        try
        {
            child = TreeWalker.ControlViewWalker.GetFirstChild(element);
        }
        catch
        {
            return;
        }

        while (child is not null && count < MaxNodes)
        {
            count++;

            try
            {
                string name = child.Current.Name;
                if (name.Length > 60)
                {
                    name = name[..60] + "…";
                }

                string extra = "";
                if (child.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePattern))
                {
                    string value = ((ValuePattern)valuePattern).Current.Value;
                    if (value.Length > 40)
                    {
                        value = value[..40] + "…";
                    }

                    extra += $" value=\"{value}\"";
                }

                try
                {
                    var patterns = child.GetSupportedPatterns();
                    if (patterns.Length > 0)
                    {
                        extra += " patterns=" + string.Join(",",
                            patterns.Select(p => p.ProgrammaticName.Replace("PatternIdentifiers.Pattern", "")));
                    }
                }
                catch
                {
                }

                log.AppendLine($"{new string(' ', depth * 2)}- {child.Current.ControlType.ProgrammaticName} " +
                               $"name=\"{name}\" focus={child.Current.HasKeyboardFocus}{extra}");
            }
            catch (Exception ex)
            {
                log.AppendLine($"{new string(' ', depth * 2)}- <ошибка: {ex.Message}>");
            }

            Walk(child, depth + 1, maxDepth, log, ref count);
            child = TreeWalker.ControlViewWalker.GetNextSibling(child);
        }
    }
}
