using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace WordPeek;

internal sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _toggleItem;

    public TrayController()
    {
        _toggleItem = new ToolStripMenuItem("Выключить перевод");
        _toggleItem.Click += (_, _) => ToggleRequested?.Invoke();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new ToolStripMenuItem("Обучение…", null, (_, _) => TrainingRequested?.Invoke()));
        menu.Items.Add(new ToolStripMenuItem("Настройки…", null, (_, _) => SettingsRequested?.Invoke()));
        menu.Items.Add(new ToolStripMenuItem("Диагностика…", null, (_, _) => DiagnosticsRequested?.Invoke()));
        menu.Items.Add(new ToolStripMenuItem("О программе…", null, (_, _) => AboutRequested?.Invoke()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Выход", null, (_, _) => ExitRequested?.Invoke()));

        _icon = new NotifyIcon
        {
            Icon = BuildIcon(),
            Visible = true,
            Text = "WordPeek",
            ContextMenuStrip = menu
        };

        _icon.DoubleClick += (_, _) => SettingsRequested?.Invoke();

        Log.Write($"tray: создан, текст=\"{_icon.Text}\"");
    }

    public event Action? ToggleRequested;

    public event Action? SettingsRequested;

    public event Action? TrainingRequested;

    public event Action? DiagnosticsRequested;

    public event Action? AboutRequested;

    public event Action? ExitRequested;

    public void SetEnabled(bool enabled)
    {
        _toggleItem.Text = enabled ? "Выключить перевод" : "Включить перевод";
        _icon.Text = enabled ? "WordPeek — перевод включён" : "WordPeek — перевод выключен";
    }

    public void Notify(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = message;
            _icon.BalloonTipIcon = icon;
            _icon.ShowBalloonTip(2500);
        }
        catch
        {
        }
    }

    private static Icon BuildIcon()
    {
        // Иконка берётся прямо из exe — та же, что видна в проводнике.
        try
        {
            string? executable = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(executable))
            {
                Icon? fromExe = Icon.ExtractAssociatedIcon(executable);
                if (fromExe is not null)
                {
                    return fromExe;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("tray icon (exe): " + ex.Message);
        }

        return BuildFallbackIcon();
    }

    private static Icon BuildFallbackIcon()
    {
        try
        {
            using Bitmap bitmap = RenderIcon();
            IntPtr handle = bitmap.GetHicon();

            try
            {
                using Icon temporary = Icon.FromHandle(handle);
                return (Icon)temporary.Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
        catch (Exception ex)
        {
            Log.Write("tray icon (fallback): " + ex.Message);
            return SystemIcons.Application;
        }
    }

    /// <summary>Сохраняет иконку трея в PNG и ICO — для проверки внешнего вида и валидности файла.</summary>
    public static string SavePreview(string pngPath)
    {
        using (Bitmap bitmap = RenderIcon())
        {
            bitmap.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
        }

        string icoPath = System.IO.Path.ChangeExtension(pngPath, ".ico");

        using (Bitmap bitmap = RenderIcon())
        {
            IntPtr handle = bitmap.GetHicon();
            try
            {
                using Icon temporary = Icon.FromHandle(handle);
                using var stream = File.Create(icoPath);
                temporary.Save(stream);
            }
            finally
            {
                DestroyIcon(handle);
            }
        }

        using var check = new Icon(icoPath);
        return $"ico {check.Width}x{check.Height}";
    }

    private static Bitmap RenderIcon()
    {
        var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var background = new SolidBrush(Color.FromArgb(45, 122, 240));
            graphics.FillEllipse(background, 1, 1, 30, 30);

            using var foreground = new SolidBrush(Color.White);
            using var font = new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
            var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            graphics.DrawString("A", font, foreground, new RectangleF(0, 0, 32, 32), format);
        }

        return bitmap;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
