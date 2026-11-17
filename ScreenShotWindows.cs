using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WpfImage = System.Windows.Controls.Image;

namespace OverlayLingo;

/// <summary>Затемнение экрана с рамкой выделения области.</summary>
internal sealed class RegionSelector : Window
{
    private readonly Canvas _canvas;
    private readonly System.Windows.Shapes.Rectangle _selection;
    private readonly Native.RECT _monitor;
    private readonly double _scale;
    private System.Windows.Point _start;
    private bool _dragging;

    public RegionSelector(Native.RECT monitor, double scale)
    {
        _monitor = monitor;
        _scale = scale <= 0 ? 1.0 : scale;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = Cursors.Cross;

        _canvas = new Canvas { Background = System.Windows.Media.Brushes.Transparent };
        _selection = new System.Windows.Shapes.Rectangle
        {
            Stroke = System.Windows.Media.Brushes.White,
            StrokeThickness = 1.5,
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, 255, 255, 255)),
            Visibility = Visibility.Collapsed
        };

        _canvas.Children.Add(_selection);
        Content = _canvas;

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        PreviewKeyDown += OnKey;
    }

    public event Action<int, int, int, int>? AreaSelected;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr handle = new WindowInteropHelper(this).Handle;
        Native.SetWindowPos(handle, Native.HWND_TOPMOST,
            _monitor.Left, _monitor.Top,
            _monitor.Right - _monitor.Left, _monitor.Bottom - _monitor.Top,
            Native.SWP_SHOWWINDOW);
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(_canvas);
        _dragging = true;

        Canvas.SetLeft(_selection, _start.X);
        Canvas.SetTop(_selection, _start.Y);
        _selection.Width = 0;
        _selection.Height = 0;
        _selection.Visibility = Visibility.Visible;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        System.Windows.Point current = e.GetPosition(_canvas);

        Canvas.SetLeft(_selection, Math.Min(_start.X, current.X));
        Canvas.SetTop(_selection, Math.Min(_start.Y, current.Y));
        _selection.Width = Math.Abs(current.X - _start.X);
        _selection.Height = Math.Abs(current.Y - _start.Y);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        System.Windows.Point end = e.GetPosition(_canvas);

        double x = Math.Min(_start.X, end.X);
        double y = Math.Min(_start.Y, end.Y);
        double width = Math.Abs(end.X - _start.X);
        double height = Math.Abs(end.Y - _start.Y);

        Close();

        if (width < 8 || height < 8)
        {
            return;
        }

        int left = _monitor.Left + (int)Math.Round(x * _scale);
        int top = _monitor.Top + (int)Math.Round(y * _scale);
        int physicalWidth = (int)Math.Round(width * _scale);
        int physicalHeight = (int)Math.Round(height * _scale);

        AreaSelected?.Invoke(left, top, physicalWidth, physicalHeight);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}

/// <summary>Показывает снимок области с переводом, нарисованным поверх строк.</summary>
internal sealed class ShotResultWindow : Window
{
    private readonly System.Windows.Threading.DispatcherTimer _timer;

    public ShotResultWindow(Bitmap composed, int left, int top, int width, int height)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;

        var image = new WpfImage
        {
            Source = ToBitmapSource(composed),
            Stretch = Stretch.Fill
        };

        var frame = new Border
        {
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Background = System.Windows.Media.Brushes.Black,
            Child = image
        };

        Content = frame;

        // Автозакрытие по таймеру: клик, Esc или 10 секунд.
        _timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Close();
        };

        Closed += (_, _) => _timer.Stop();
        Loaded += (_, _) => _timer.Start();

        MouseLeftButtonDown += (_, _) => Close();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };

        Loaded += (_, _) =>
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowPos(handle, Native.HWND_TOPMOST, left, top, width, height, Native.SWP_SHOWWINDOW);
        };
    }

    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        IntPtr handle = bitmap.GetHbitmap();

        try
        {
            return Imaging.CreateBitmapSourceFromHBitmap(
                handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            DeleteObject(handle);
        }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
