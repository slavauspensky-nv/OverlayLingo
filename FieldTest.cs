using System.IO;
using System.Text;

namespace WordPeek;

/// <summary>Проверяет извлечение поля из конкретного окна (по имени процесса или активного).</summary>
internal static class FieldTest
{
    public static void Run(string? processName)
    {
        var log = new StringBuilder();

        IntPtr hwnd = string.IsNullOrWhiteSpace(processName)
            ? Native.GetForegroundWindow()
            : TreeDump.FindWindowOfProcess(processName);

        log.AppendLine($"target=\"{processName ?? "(активное окно)"}\" hwnd={hwnd}");

        if (hwnd == IntPtr.Zero)
        {
            log.AppendLine("окно не найдено");
        }
        else
        {
            var snapshot = FieldReader.ReadFromHandle(hwnd);
            log.AppendLine($"field={snapshot.IsTextField} pwd={snapshot.IsPassword} " +
                           $"caret={snapshot.HasCaret} ({snapshot.CaretX},{snapshot.CaretY},{snapshot.CaretH}) " +
                           $"anchor=({snapshot.AnchorX},{snapshot.AnchorY}) " +
                           $"type={snapshot.ControlTypeName} proc={snapshot.ProcessName}");
            log.AppendLine($"text=\"{snapshot.Text.Replace("\r", " ").Replace("\n", " ")}\"");
        }

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "fieldtest.log"), log.ToString());
    }
}
