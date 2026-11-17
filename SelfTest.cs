using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;

namespace OverlayLingo;

/// <summary>
/// Проверяет, что UIA реально отдаёт текст и прямоугольник каретки для обычного поля ввода.
/// Результат пишется в selftest.log.
/// </summary>
internal static class SelfTest
{
    public static void Run()
    {
        var log = new StringBuilder();

        var form = new Form
        {
            Text = "OverlayLingo self test",
            Width = 560,
            Height = 200,
            StartPosition = FormStartPosition.CenterScreen
        };

        var box = new TextBox
        {
            Multiline = true,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 14f),
            Text = "Hello world how are you"
        };

        form.Controls.Add(box);
        form.Shown += (_, _) =>
        {
            box.Focus();
            box.SelectionStart = box.Text.Length;
        };

        form.Show();
        Pump(1200);

        TestMultiline(form, log);

        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is not null)
            {
                string proc = "?";
                try { proc = System.Diagnostics.Process.GetProcessById(focused.Current.ProcessId).ProcessName; } catch { }
                log.AppendLine($"focused: type={focused.Current.ControlType.ProgrammaticName} proc={proc} hwnd={focused.Current.NativeWindowHandle}");
            }
            else
            {
                log.AppendLine("focused: null");
            }

            var element = AutomationElement.FromHandle(box.Handle);
            log.AppendLine($"name={element.Current.Name} type={element.Current.ControlType.ProgrammaticName}");

            var read = FieldReader.ReadFromHandle(box.Handle, allowOwnProcess: true);
            log.AppendLine($"reader: field={read.IsTextField} caret={read.HasCaret} " +
                           $"({read.CaretX},{read.CaretY},{read.CaretH}) text=\"{read.Text}\"");

            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePattern))
            {
                log.AppendLine("value=" + ((ValuePattern)valuePattern).Current.Value);
            }
            else
            {
                log.AppendLine("no ValuePattern");
            }

            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object textPatternObject))
            {
                var textPattern = (TextPattern)textPatternObject;
                log.AppendLine("text=" + textPattern.DocumentRange.GetText(-1));

                var selection = textPattern.GetSelection();
                log.AppendLine("selection=" + selection.Length);

                if (selection.Length > 0)
                {
                    Dump("raw", selection[0].GetBoundingRectangles(), log);

                    var forward = selection[0].Clone();
                    forward.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 1);
                    Dump("forward", forward.GetBoundingRectangles(), log);

                    var backward = selection[0].Clone();
                    backward.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -1);
                    Dump("backward", backward.GetBoundingRectangles(), log);
                }
            }
            else
            {
                log.AppendLine("no TextPattern");
            }
        }
        catch (Exception ex)
        {
            log.AppendLine("ERR " + ex);
        }

        Pump(300);
        form.Close();

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "selftest.log"), log.ToString());
    }

    private static void Dump(string label, System.Windows.Rect[] rects, StringBuilder log)
    {
        log.AppendLine($"{label}: {rects.Length} rect(s) " +
                       string.Join("; ", rects.Select(r => $"[{r.X:0},{r.Y:0} {r.Width:0}x{r.Height:0}]")));
    }

    /// <summary>
    /// Проверяет путь для «документов» (Word): многострочный текст, каретка в середине,
    /// чтение через TextPattern с абзацем вокруг каретки.
    /// </summary>
    private static void TestMultiline(Form form, StringBuilder log)
    {
        log.AppendLine("--- многострочное поле ---");

        var document = new RichTextBox
        {
            Dock = DockStyle.Bottom,
            Height = 160,
            Font = new Font("Segoe UI", 12f),
            Text = "First line here\nSecond line with words\nThird line here\nFourth line"
        };

        form.Controls.Add(document);
        document.BringToFront();
        Pump(300);

        document.Focus();
        document.SelectionStart = document.Text.IndexOf("Second", StringComparison.Ordinal) + 6;
        Park(400);

        try
        {
            var read = FieldReader.ReadFromHandle(document.Handle, allowOwnProcess: true);
            log.AppendLine($"reader: field={read.IsTextField} caret={read.HasCaret} " +
                           $"({read.CaretX},{read.CaretY},{read.CaretH}) anchorTop={read.AnchorTop} " +
                           $"text=\"{read.Text.Replace("\n", "|")}\"");
        }
        catch (Exception ex)
        {
            log.AppendLine("ERR " + ex.Message);
        }

        document.Dispose();
        Pump(200);
    }

    private static void Park(int milliseconds)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < end)
        {
            Application.DoEvents();
            Thread.Sleep(25);
        }
    }

    private static void Pump(int milliseconds)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < end)
        {
            Application.DoEvents();
            Thread.Sleep(30);
        }
    }
}
