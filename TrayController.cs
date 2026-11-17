using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace OverlayLingo;

internal sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _toggleItem;

    public TrayController()
    {
        _toggleItem = new ToolStripMenuItem("Живой перевод из поля") { CheckOnClick = false };
        _toggleItem.Click += (_, _) => ToggleRequested?.Invoke();

        var shot = new ToolStripMenuItem("Снимок области с переводом", null, (_, _) => ShotRequested?.Invoke());

        var menu = new ContextMenuStrip { ShowItemToolTips = true };
        menu.Items.Add(shot);
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Тренировка слов…", null, (_, _) => TrainingRequested?.Invoke()));
        menu.Items.Add(new ToolStripMenuItem("Настройки…", null, (_, _) => SettingsRequested?.Invoke()));
        menu.Items.Add(new ToolStripMenuItem("Состояние и диагностика…", null, (_, _) => DiagnosticsRequested?.Invoke()));
        menu.Items.Add(new ToolStripMenuItem("О программе…", null, (_, _) => AboutRequested?.Invoke()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Выход", null, (_, _) => ExitRequested?.Invoke()));

        shot.ToolTipText = "Обвести область экрана и получить перевод";
        _toggleItem.ToolTipText = "Перевод по мере набора текста в поле ввода";

        _icon = new NotifyIcon
        {
            Icon = BuildIcon(),
            Visible = true,
            Text = "OverlayLingo",
            ContextMenuStrip = menu
        };

        _icon.DoubleClick += (_, _) => SettingsRequested?.Invoke();

        Log.Write($"tray: создан, текст=\"{_icon.Text}\"");
    }

    public event Action? ToggleRequested;

    public event Action? ShotRequested;

    public event Action? SettingsRequested;

    public event Action? TrainingRequested;

    public event Action? DiagnosticsRequested;

    public event Action? AboutRequested;

    public event Action? ExitRequested;

    public void SetEnabled(bool enabled)
    {
        _toggleItem.Checked = enabled;
        _toggleItem.Text = enabled ? "Живой перевод из поля — включён" : "Живой перевод из поля — выключен";
        _icon.Text = enabled ? "OverlayLingo — живой перевод включён" : "OverlayLingo — живой перевод выключен";
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
        // Запасная иконка повторяет app.ico: индиго-плитка, белая «A», янтарная каретка.
        var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);

            using var tile = RoundedRect(new RectangleF(0.5f, 0.5f, 31f, 31f), 6f);
            using var background = new SolidBrush(Color.FromArgb(48, 82, 214));
            graphics.FillPath(background, tile);

            using var foreground = new SolidBrush(Color.White);
            using var font = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
            var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            graphics.DrawString("A", font, foreground, new RectangleF(0f, -1.5f, 32f, 26f), format);

            using var caret = new SolidBrush(Color.FromArgb(255, 176, 32));
            using var caretPath = RoundedRect(new RectangleF(10f, 23f, 12f, 3f), 1.5f);
            graphics.FillPath(caret, caretPath);
        }

        return bitmap;
    }

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
