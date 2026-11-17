using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace WordPeek;

/// <summary>
/// Плавающая строка перевода. Не забирает фокус, клик — копирование.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private const int Gap = 5;
    private const int ScreenMargin = 8;

    private readonly TextBlock _text;
    private readonly Border _root;
    private string _current = "";
    private bool _everShown;

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 620;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Focusable = false;
        Opacity = 0;

        _text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 15,
            Foreground = Brushes.White,
            MaxWidth = 580,
            LineHeight = 20
        };

        var hint = new TextBlock
        {
            Text = "клик — скопировать · правый клик — заменить",
            FontSize = 10.5,
            Foreground = Brushes.White,
            Opacity = 0.5,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var stack = new StackPanel();
        stack.Children.Add(_text);
        stack.Children.Add(hint);

        _root = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x1B, 0x1D, 0x21)),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(13, 10, 13, 9),
            Child = stack,
            Cursor = Cursors.Hand,
            Effect = new DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 2,
                Opacity = 0.35,
                Direction = 270
            }
        };

        _root.MouseLeftButtonDown += (_, _) =>
        {
            string translation = _current;
            HideTranslation();
            CopyRequested?.Invoke(translation);
        };

        _root.MouseRightButtonDown += (_, _) =>
        {
            string translation = _current;
            HideTranslation();
            ReplaceRequested?.Invoke(translation);
        };

        Content = _root;
    }

    public event Action<string>? CopyRequested;

    public event Action<string>? ReplaceRequested;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr handle = new WindowInteropHelper(this).Handle;
        int style = Native.GetWindowLong(handle, Native.GWL_EXSTYLE);
        Native.SetWindowLong(handle, Native.GWL_EXSTYLE,
            style | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
    }

    public void ShowTranslation(string translation, int anchorX, int anchorBottom, int anchorTop)
    {
        if (string.IsNullOrWhiteSpace(translation))
        {
            return;
        }

        _current = translation;
        _text.Text = translation;

        bool hidden = !_everShown || !IsVisible;
        if (hidden)
        {
            Opacity = 0;
            Show();
            _everShown = true;
        }

        UpdateLayout();
        PlaceAt(anchorX, anchorBottom, anchorTop);

        Opacity = 1;
    }

    public void Reposition(int anchorX, int anchorBottom, int anchorTop)
    {
        if (!IsVisible)
        {
            return;
        }

        PlaceAt(anchorX, anchorBottom, anchorTop);
    }

    public void HideTranslation()
    {
        if (!IsVisible)
        {
            return;
        }

        Opacity = 0;
        Hide();
    }

    /// <summary>Сохраняет внешний вид плашки в PNG — для проверки отрисовки.</summary>
    public void SavePreview(string path)
    {
        UpdateLayout();

        int width = (int)Math.Ceiling(_root.ActualWidth);
        int height = (int)Math.Ceiling(_root.ActualHeight);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(_root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private void PlaceAt(int anchorX, int anchorBottom, int anchorTop)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        double dpi = Native.GetDpi(handle);
        double windowScale = dpi / 96.0;
        double scale = Native.GetScaleAt(anchorX, anchorBottom);
        if (Math.Abs(scale - windowScale) < 0.01)
        {
            scale = windowScale;
        }

        int width = (int)Math.Ceiling(ActualWidth * scale);
        int height = (int)Math.Ceiling(ActualHeight * scale);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        int left = anchorX;
        int top = anchorBottom + Gap;

        Native.RECT work = Native.GetWorkArea(left, top);

        int maxLeft = work.Right - width - ScreenMargin;
        if (left > maxLeft)
        {
            left = maxLeft;
        }

        if (left < work.Left + ScreenMargin)
        {
            left = work.Left + ScreenMargin;
        }

        // Не влезли снизу — показываем над строкой.
        if (top + height > work.Bottom - ScreenMargin)
        {
            int above = anchorTop - height - Gap;
            top = above >= work.Top + ScreenMargin
                ? above
                : Math.Max(work.Top + ScreenMargin, work.Bottom - height - ScreenMargin);
        }

        if (top < work.Top + ScreenMargin)
        {
            top = work.Top + ScreenMargin;
        }

        Native.SetWindowPos(handle, Native.HWND_TOPMOST, left, top, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }
}
