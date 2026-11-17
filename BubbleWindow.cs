using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace OverlayLingo;

/// <summary>Маленькая подсказка с обратным переводом слова.</summary>
internal sealed class WordTipWindow : Window
{
    public WordTipWindow(string text, AppSettings settings)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 360;
        WindowStartupLocation = WindowStartupLocation.Manual;
        IsHitTestVisible = false;

        Color foreground = StyleHelper.ParseColor(settings.BubbleFontColor, Colors.White);
        Color background = StyleHelper.ParseColor(settings.BubbleBackground, Color.FromRgb(0x1B, 0x1D, 0x21));
        double opacity = Math.Clamp(settings.BubbleOpacity, 0.2, 1.0);
        byte alpha = (byte)Math.Round(opacity * 255);
        bool light = StyleHelper.IsLight(background);

        var label = new TextBlock
        {
            Text = text,
            FontFamily = FontHelper.Resolve(settings.BubbleFont),
            FontSize = Math.Clamp(settings.BubbleFontSize, 9, 48),
            Foreground = new SolidColorBrush(foreground),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320
        };

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(alpha, background.R, background.G, background.B)),
            BorderBrush = new SolidColorBrush(light
                ? Color.FromArgb(0x40, 0, 0, 0)
                : Color.FromArgb(0x40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 6, 12, 7),
            Child = label,
            Effect = new DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 2,
                Opacity = 0.4,
                Direction = 270
            }
        };

        Content = border;
    }

    public void ShowAbove(int screenX, int screenY)
    {
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();

        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        double scale = Native.GetScaleAt(screenX, screenY);
        int width = (int)Math.Ceiling(ActualWidth * scale);
        int height = (int)Math.Ceiling(ActualHeight * scale);

        int left = screenX - width / 2;
        int top = screenY - height - 4;

        Native.RECT work = Native.GetWorkArea(screenX, screenY);

        if (top < work.Top + 4)
        {
            top = screenY + 20;
        }

        if (left < work.Left + 4)
        {
            left = work.Left + 4;
        }

        if (left + width > work.Right - 4)
        {
            left = work.Right - width - 4;
        }

        Native.SetWindowPos(handle, Native.HWND_TOPMOST, left, top, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }
}

/// <summary>Окно перевода: перетаскивается, оформление настраивается.</summary>
internal sealed class BubbleWindow : Window
{
    private const int Gap = 10;
    private const int Edge = 8;
    private static readonly TimeSpan DoubleClickWindow = TimeSpan.FromMilliseconds(420);

    private readonly AppSettings _settings;
    private readonly TextBlock _text;
    private TextBlock? _hint;
    private readonly Border _collapseButton;
    private readonly TextBlock _collapseIcon = new();
    private Border? _content;
    private ScrollViewer? _scroller;
    private double _savedHeight;
    private double _savedWidth;
    private SizeToContent _savedSizeToContent;
    private Border? _statusBorder;
    private Border? _footer;
    private bool _collapsed;
    private readonly TextBlock _status;
    private readonly string _fullText;
    private readonly int _typeSpeed;
    private WordTipWindow? _tip;
    private System.Windows.Point _tipAnchor;
    private System.Windows.Point _tipOrigin;
    private bool _tipAnchorValid;
    private DispatcherTimer? _tipTimer;
    private DispatcherTimer? _typingTimer;
    private double _typingAccumulator;
    private int _visibleChars;
    private string _lastWord = "";
    private DateTime _lastClickAt = DateTime.MinValue;
    private DateTime _pauseUntil = DateTime.MinValue;
    private bool _dragging;
    private Native.POINT _dragStart;
    private Native.RECT _dragOrigin;
    private int _resizeEdges;
    private Native.POINT _resizeStart;
    private Native.RECT _resizeOrigin;
    private bool _cursorOnEdge;

    public BubbleWindow(string text, AppSettings settings,
        int regionLeft, int regionTop, int regionRight, int regionBottom)
    {
        _settings = settings;
        _fullText = text;
        _typeSpeed = settings.BubbleTyping
            ? Math.Clamp(settings.BubbleTypeSpeed, 0, 400)
            : 0;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 480;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = Cursors.Arrow;

        // Размер, который пользователь выставил вручную.
        if (settings.BubbleWidth >= 220 && settings.BubbleHeight >= 90)
        {
            SizeToContent = SizeToContent.Manual;
            Width = settings.BubbleWidth;
            Height = settings.BubbleHeight;
        }

        if (SizeToContent == SizeToContent.Manual)
        {
            // Свободный размер: текст подстраивается под ширину окна.
            MaxWidth = double.PositiveInfinity;
        }

        var fontFamily = FontHelper.Resolve(settings.BubbleFont);
        var foreground = StyleHelper.ParseColor(settings.BubbleFontColor, Colors.White);
        var background = StyleHelper.ParseColor(settings.BubbleBackground, Color.FromRgb(0x1B, 0x1D, 0x21));

        double opacity = Math.Clamp(settings.BubbleOpacity, 0.2, 1.0);
        byte alpha = (byte)Math.Round(opacity * 255);

        _text = new TextBlock
        {
            FontFamily = fontFamily,
            FontSize = Math.Clamp(settings.BubbleFontSize, 9, 48),
            LineHeight = Math.Clamp(settings.BubbleFontSize, 9, 48) * 1.38,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(foreground)
        };

        BuildWords(text);

        if (_typeSpeed > 0 && _fullText.Length > 0)
        {
            // Печатаем слева направо: сначала пусто, потом по несколько символов за такт.
            _text.Inlines.Clear();
            _visibleChars = 0;

            _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _typingTimer.Tick += OnTypingTick;
            _typingTimer.Start();
        }
        else
        {
            _visibleChars = _fullText.Length;
            BuildWords(_fullText);
        }

        _status = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(foreground),
            Opacity = 0,
            Margin = new Thickness(0, 6, 0, 0)
        };

        var hint = new TextBlock
        {
            Text = "клик по слову — обратный перевод · двойной клик — в блокнот · Esc — закрыть",
            FontSize = 10.5,
            Opacity = 0.78,
            Foreground = new SolidColorBrush(foreground),
            TextWrapping = TextWrapping.Wrap,
            
        };

        _hint = hint;

        var title = new TextBlock
        {
            Text = "ПЕРЕВОД",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.72,
            Foreground = new SolidColorBrush(foreground),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0)
        };

        Border close = CreateIconButton("✕", foreground, background,
            "Закрыть и вернуть позицию по умолчанию", ResetPositionAndClose);

        _collapseIcon = new TextBlock();
        Border collapse = CreateIconButton("—", foreground, background,
            "Свернуть до заголовка", ToggleCollapsed, _collapseIcon);
        collapse.Margin = new Thickness(0, 0, 4, 0);
        _collapseButton = collapse;

        var bar = new Grid { Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(title, 0);
        Grid.SetColumn(_collapseButton, 1);
        Grid.SetColumn(close, 2);
        bar.Children.Add(title);
        bar.Children.Add(_collapseButton);
        bar.Children.Add(close);
        bar.Cursor = Cursors.SizeAll;

        // Перенос тащим вручную: окно не активируется, поэтому DragMove() не работает.
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (!Native.GetCursorPos(out _dragStart))
            {
                return;
            }

            Native.GetWindowRect(new WindowInteropHelper(this).Handle, out _dragOrigin);
            _dragging = true;
            bar.CaptureMouse();
            e.Handled = true;
        };

        bar.MouseMove += (_, _) =>
        {
            if (!_dragging || !Native.GetCursorPos(out Native.POINT current))
            {
                return;
            }

            int left = _dragOrigin.Left + (current.X - _dragStart.X);
            int top = _dragOrigin.Top + (current.Y - _dragStart.Y);

            Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST,
                left, top, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

            SyncTip();
        };

        bar.MouseLeftButtonUp += (_, _) =>
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            bar.ReleaseMouseCapture();
            SavePosition();
        };

        // Тонкие разделители: светлее — на светлом фоне, темнее — на тёмном.
        double luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        bool lightBackground = luminance > 0.6;
        byte lineAlpha = 0x2E;
        var lineColor = lightBackground
            ? Color.FromArgb(lineAlpha, 0, 0, 0)
            : Color.FromArgb(lineAlpha, 255, 255, 255);

        var header = new Border
        {
            Background = new SolidColorBrush(lightBackground
                ? Color.FromArgb(0x14, 0, 0, 0)
                : Color.FromArgb(0x16, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(lineColor),
            BorderThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(14, 14, 0, 0),
            Padding = new Thickness(14, 6, 8, 6),
            Child = bar
        };

        var content = new Border
        {
            Padding = new Thickness(16, 11, 16, 11),
            Child = _text
        };

        var statusBorder = new Border
        {
            Padding = new Thickness(16, 0, 16, 0),
            Child = _status
        };

        var footer = new Border
        {
            Background = new SolidColorBrush(lightBackground
                ? Color.FromArgb(0x1A, 0, 0, 0)
                : Color.FromArgb(0x1E, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(lineColor),
            BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(0, 0, 14, 14),
            Padding = new Thickness(16, 7, 16, 8),
            Child = hint
        };

        _content = content;
        _statusBorder = statusBorder;
        _footer = footer;

        var stack = new StackPanel();
        stack.Children.Add(content);
        stack.Children.Add(statusBorder);

        // Подвал прибит к низу, текст занимает всё остальное и прокручивается.
        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = stack
        };

        // ScrollViewer перехватывает клики, поэтому закрытие вешаем на предпросмотр:
        // клик по слову пропускаем, любой другой — закрывает окно.
        scroller.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is Run)
            {
                return;
            }

            Close();
        };

        _scroller = scroller;

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(header, 0);
        Grid.SetRow(scroller, 1);
        Grid.SetRow(footer, 2);

        layout.Children.Add(header);
        layout.Children.Add(scroller);
        layout.Children.Add(footer);

        var body = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(alpha, background.R, background.G, background.B)),
            CornerRadius = new CornerRadius(14),
            Child = layout,
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 3,
                Opacity = 0.45,
                Direction = 270
            }
        };

        Content = body;

        // Изменение размера — за края окна, без уголка-ручки.
        PreviewMouseLeftButtonDown += OnResizeStart;
        PreviewMouseMove += OnResizeMove;
        PreviewMouseLeftButtonUp += OnResizeEnd;

        // Клик по любому месту окна (кроме слова и крестика) закрывает его,
        // но позицию не сбрасывает — сбрасывает только крестик.
        body.MouseLeftButtonDown += (_, _) => Close();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };

        Closed += (_, _) =>
        {
            _typingTimer?.Stop();
            _typingTimer = null;
            _tipTimer?.Stop();
            _tipTimer = null;
            _tip?.Close();
            _tip = null;
        };

        Loaded += (_, _) => Place(regionLeft, regionTop, regionRight, regionBottom);
    }

    public event Action<string, Point>? WordClicked;

    public event Action<string>? WordActivated;

    public void ShowTip(string translation, Point screenPoint)
    {
        if (string.IsNullOrWhiteSpace(translation))
        {
            return;
        }

        _tip?.Close();
        _tip = new WordTipWindow(translation, _settings);
        _tip.Show();
        _tip.ShowAbove((int)screenPoint.X, (int)screenPoint.Y);

        Log.Write($"word: подсказка \"{translation}\" в {screenPoint.X:0},{screenPoint.Y:0}, видима={_tip.IsVisible}");

        // Запоминаем, где окно стояло в момент показа: дальше подсказка едет за ним.
        _tipAnchor = screenPoint;
        _tipAnchorValid = Native.GetWindowRect(new WindowInteropHelper(this).Handle, out Native.RECT origin);
        _tipOrigin = new System.Windows.Point(origin.Left, origin.Top);

        _tipTimer?.Stop();

        _tipTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        _tipTimer.Tick += (_, _) =>
        {
            _tipTimer?.Stop();
            _tipTimer = null;
            _tip?.Close();
            _tip = null;
            _tipAnchorValid = false;
        };
        _tipTimer.Start();
    }

    /// <summary>Двигает подсказку синхронно с окном перевода.</summary>
    private void SyncTip()
    {
        if (_tip is null || !_tipAnchorValid)
        {
            return;
        }

        if (!Native.GetWindowRect(new WindowInteropHelper(this).Handle, out Native.RECT current))
        {
            return;
        }

        double dx = current.Left - _tipOrigin.X;
        double dy = current.Top - _tipOrigin.Y;

        _tip.ShowAbove((int)Math.Round(_tipAnchor.X + dx), (int)Math.Round(_tipAnchor.Y + dy));
    }

    public void ShowStatus(string message)
    {
        _status.Text = message;
        _status.Opacity = 1;

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _status.Opacity = 0;
        };
        timer.Start();
    }

    private static FontFamily SafeFont(string name)
    {
        try
        {
            return new FontFamily(string.IsNullOrWhiteSpace(name) ? "Segoe UI" : name);
        }
        catch
        {
            return new FontFamily("Segoe UI");
        }
    }

    private static Color SafeColor(string hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
            {
                var parsed = (Color)ColorConverter.ConvertFromString(hex);
                return Color.FromRgb(parsed.R, parsed.G, parsed.B);
            }
        }
        catch
        {
        }

        return fallback;
    }

    /// <summary>Маленькая иконка-кнопка: при наведении заливается цветом шрифта, глиф инвертируется.</summary>
    private static Border CreateIconButton(string glyph, Color foreground, Color panelBackground,
        string tooltip, Action onClick, TextBlock? icon = null)
    {
        var text = icon ?? new TextBlock();
        text.Text = glyph;
        text.FontSize = 11;
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Foreground = new SolidColorBrush(foreground);

        var border = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Child = text,
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var hover = new SolidColorBrush(foreground);
        var inverted = new SolidColorBrush(panelBackground);

        border.MouseEnter += (_, _) =>
        {
            border.Background = hover;
            text.Foreground = inverted;
        };

        border.MouseLeave += (_, _) =>
        {
            border.Background = Brushes.Transparent;
            text.Foreground = new SolidColorBrush(foreground);
        };

        border.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };

        return border;
    }

    /// <summary>Какие края окна под курсором: 1 лево, 2 право, 4 верх, 8 низ.</summary>
    private static int EdgeAt(Native.POINT cursor, Native.RECT rect, double scale)
    {
        int zone = Math.Max((int)Math.Round(6 * scale), 4);
        int edges = 0;

        if (Math.Abs(cursor.X - rect.Left) <= zone)
        {
            edges |= 1;
        }

        if (Math.Abs(cursor.X - rect.Right) <= zone)
        {
            edges |= 2;
        }

        if (Math.Abs(cursor.Y - rect.Top) <= zone)
        {
            edges |= 4;
        }

        if (Math.Abs(cursor.Y - rect.Bottom) <= zone)
        {
            edges |= 8;
        }

        return edges;
    }

    private void OnResizeStart(object sender, MouseButtonEventArgs e)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;

        if (handle == IntPtr.Zero || !Native.GetCursorPos(out Native.POINT cursor))
        {
            return;
        }

        if (!Native.GetWindowRect(handle, out Native.RECT rect))
        {
            return;
        }

        double scale = Native.GetScaleAt(cursor.X, cursor.Y);
        int edges = EdgeAt(cursor, rect, scale);

        if (edges == 0)
        {
            return;
        }

        _resizeEdges = edges;
        _resizeStart = cursor;
        _resizeOrigin = rect;

        CaptureMouse();
        e.Handled = true;
    }

    private void OnResizeMove(object sender, MouseEventArgs e)
    {
        if (_resizeEdges == 0)
        {
            UpdateResizeCursor();
            return;
        }

        if (!Native.GetCursorPos(out Native.POINT cursor))
        {
            return;
        }

        ApplyResize(cursor);
        e.Handled = true;
    }

    private void OnResizeEnd(object sender, MouseButtonEventArgs e)
    {
        if (_resizeEdges == 0)
        {
            return;
        }

        _resizeEdges = 0;
        ReleaseMouseCapture();
        SaveSize();
        e.Handled = true;
    }

    private void ApplyResize(Native.POINT cursor)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;

        if (handle == IntPtr.Zero)
        {
            return;
        }

        int left = _resizeOrigin.Left;
        int top = _resizeOrigin.Top;
        int right = _resizeOrigin.Right;
        int bottom = _resizeOrigin.Bottom;

        int dx = cursor.X - _resizeStart.X;
        int dy = cursor.Y - _resizeStart.Y;

        if ((_resizeEdges & 1) != 0)
        {
            left += dx;
        }

        if ((_resizeEdges & 2) != 0)
        {
            right += dx;
        }

        if ((_resizeEdges & 4) != 0)
        {
            top += dy;
        }

        if ((_resizeEdges & 8) != 0)
        {
            bottom += dy;
        }

        double scale = Native.GetScaleAt(cursor.X, cursor.Y);
        int minWidth = (int)(220 * scale);
        int minHeight = (int)(90 * scale);

        if (right - left < minWidth)
        {
            if ((_resizeEdges & 1) != 0)
            {
                left = right - minWidth;
            }
            else
            {
                right = left + minWidth;
            }
        }

        if (bottom - top < minHeight)
        {
            if ((_resizeEdges & 4) != 0)
            {
                top = bottom - minHeight;
            }
            else
            {
                bottom = top + minHeight;
            }
        }

        if (SizeToContent != SizeToContent.Manual)
        {
            // Переходим к свободному размеру: текст теперь подстраивается под окно.
            SizeToContent = SizeToContent.Manual;
            MaxWidth = double.PositiveInfinity;
            Width = (right - left) / scale;
            Height = (bottom - top) / scale;
        }

        Native.SetWindowPos(handle, Native.HWND_TOPMOST, left, top, right - left, bottom - top,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    private void UpdateResizeCursor()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;

        if (handle == IntPtr.Zero
            || !Native.GetCursorPos(out Native.POINT cursor)
            || !Native.GetWindowRect(handle, out Native.RECT rect))
        {
            return;
        }

        double scale = Native.GetScaleAt(cursor.X, cursor.Y);
        int edges = EdgeAt(cursor, rect, scale);

        if (edges == 0)
        {
            // Не перебиваем курсор-руку над словами.
            if (_cursorOnEdge)
            {
                Cursor = Cursors.Arrow;
                _cursorOnEdge = false;
            }

            return;
        }

        Cursor = edges switch
        {
            1 or 2 => Cursors.SizeWE,
            4 or 8 => Cursors.SizeNS,
            5 or 10 => Cursors.SizeNWSE,
            6 or 9 => Cursors.SizeNESW,
            _ => Cursors.SizeAll
        };

        _cursorOnEdge = true;
    }

    private void SaveSize()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;

        if (handle == IntPtr.Zero || !Native.GetWindowRect(handle, out Native.RECT rect))
        {
            return;
        }

        double scale = Native.GetScaleAt(rect.Left, rect.Top);

        _settings.BubbleWidth = (rect.Right - rect.Left) / scale;
        _settings.BubbleHeight = (rect.Bottom - rect.Top) / scale;
        _settings.BubbleLeft = rect.Left;
        _settings.BubbleTop = rect.Top;
        _settings.Save();

        Log.Write($"bubble: размер {_settings.BubbleWidth:0}x{_settings.BubbleHeight:0}");
    }

    /// <summary>Свернуть до заголовка и обратно.</summary>
    private void ToggleCollapsed()
    {
        _collapsed = !_collapsed;

        if (_collapsed)
        {
            // Подсказка к слову при свёрнутом окне не имеет смысла.
            _tipTimer?.Stop();
            _tipTimer = null;
            _tip?.Close();
            _tip = null;
            _tipAnchorValid = false;

            _savedWidth = ActualWidth;
            _savedHeight = ActualHeight;
            _savedSizeToContent = SizeToContent;

            var hidden = Visibility.Collapsed;

            if (_scroller is not null)
            {
                _scroller.Visibility = hidden;
            }

            if (_content is not null)
            {
                _content.Visibility = hidden;
            }

            if (_statusBorder is not null)
            {
                _statusBorder.Visibility = hidden;
            }

            if (_footer is not null)
            {
                _footer.Visibility = hidden;
            }

            // Сворачиваем строго до шапки, даже если окно было растянуто.
            if (_savedSizeToContent == SizeToContent.Manual)
            {
                Width = _savedWidth;
                SizeToContent = SizeToContent.Height;
                Height = double.NaN;
            }
            else
            {
                // В авторежиме не даём окну сузиться: шапка остаётся во всю ширину.
                MinWidth = _savedWidth;
            }
        }
        else
        {
            var shown = Visibility.Visible;

            if (_scroller is not null)
            {
                _scroller.Visibility = shown;
            }

            if (_content is not null)
            {
                _content.Visibility = shown;
            }

            if (_statusBorder is not null)
            {
                _statusBorder.Visibility = shown;
            }

            if (_footer is not null)
            {
                _footer.Visibility = shown;
            }

            if (_savedSizeToContent == SizeToContent.Manual)
            {
                SizeToContent = SizeToContent.Manual;
                Width = _savedWidth;
                Height = _savedHeight;
            }
            else
            {
                MinWidth = 0;
            }
        }

        _collapseIcon.Text = _collapsed ? "▾" : "—";
        _collapseIcon.ToolTip = _collapsed ? "Развернуть" : "Свернуть до заголовка";
    }

    private void ResetPositionAndClose()
    {
        _settings.BubbleLeft = -1;
        _settings.BubbleTop = -1;
        _settings.Save();
        Close();
    }

    private void SavePosition()
    {
        if (!Native.GetWindowRect(new WindowInteropHelper(this).Handle, out Native.RECT rect))
        {
            return;
        }

        _settings.BubbleLeft = rect.Left;
        _settings.BubbleTop = rect.Top;
        _settings.Save();

        Log.Write($"bubble: позиция запомнена {rect.Left}x{rect.Top}");
    }

    private void OnTypingTick(object? sender, EventArgs e)
    {
        // Пауза после точки или запятой — как при настоящем наборе.
        if (DateTime.UtcNow < _pauseUntil)
        {
            return;
        }

        _typingAccumulator += _typeSpeed * 0.016;

        int step = (int)_typingAccumulator;
        if (step <= 0)
        {
            return;
        }

        _typingAccumulator -= step;

        int previous = _visibleChars;
        _visibleChars = Math.Min(_fullText.Length, _visibleChars + step);

        if (_visibleChars <= previous)
        {
            return;
        }

        string added = _fullText[previous.._visibleChars];

        BuildWords(_fullText[.._visibleChars]);

        char lastVisible = added.TrimEnd().Length > 0 ? added.TrimEnd()[^1] : ' ';

        // Щелчок на появление символов, но не на пробелы.
        if (_settings.BubbleSound && !string.IsNullOrWhiteSpace(added))
        {
            TypingSound.Play(_settings.BubbleSoundVariant, _settings.BubbleSoundVolume);
        }

        if (lastVisible is '.' or '!' or '?')
        {
            _pauseUntil = DateTime.UtcNow.AddMilliseconds(240);
        }
        else if (lastVisible is ',' or ';' or ':')
        {
            _pauseUntil = DateTime.UtcNow.AddMilliseconds(120);
        }

        if (_visibleChars >= _fullText.Length)
        {
            _typingTimer?.Stop();
            _typingTimer = null;
        }
    }

    private void BuildWords(string text)
    {
        _text.Inlines.Clear();

        var word = new System.Text.StringBuilder();

        foreach (char symbol in text)
        {
            if (char.IsWhiteSpace(symbol))
            {
                FlushWord(word);
                _text.Inlines.Add(new Run(symbol.ToString()));
                continue;
            }

            word.Append(symbol);
        }

        FlushWord(word);
    }

    private void FlushWord(System.Text.StringBuilder word)
    {
        if (word.Length == 0)
        {
            return;
        }

        string value = word.ToString().Trim();
        word.Clear();

        var cleaned = new string(value.Where(c => !",.;:!?()[]\"'«»—–".Contains(c)).ToArray());
        if (cleaned.Length == 0)
        {
            _text.Inlines.Add(new Run(value));
            return;
        }

        var run = new Run(value) { Cursor = Cursors.Hand };
        run.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            OnWordClick(cleaned, run);
        };

        _text.Inlines.Add(run);
    }

    private void OnWordClick(string word, Run run)
    {
        Log.Write($"word: клик \"{word}\"");

        DateTime now = DateTime.UtcNow;
        bool isDouble = word == _lastWord && now - _lastClickAt < DoubleClickWindow;

        _lastWord = word;
        _lastClickAt = now;

        if (isDouble)
        {
            _lastClickAt = DateTime.MinValue;
            WordActivated?.Invoke(word);
            return;
        }

        try
        {
            Rect rect = run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if (rect.IsEmpty)
            {
                return;
            }

            Point screen = _text.PointToScreen(new Point(rect.X + rect.Width / 2, rect.Y));
            WordClicked?.Invoke(word, screen);
        }
        catch
        {
        }
    }

    private void Place(int regionLeft, int regionTop, int regionRight, int regionBottom)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // Если пользователь перетащил окно — встаём туда, пока не нажали крестик.
        int left;
        int top;
        int width;
        int height;

        double scale = Native.GetScaleAt(regionLeft, regionTop);
        width = (int)Math.Ceiling(ActualWidth * scale);
        height = (int)Math.Ceiling(ActualHeight * scale);

        if (_settings.BubbleLeft >= 0 && _settings.BubbleTop >= 0)
        {
            left = _settings.BubbleLeft;
            top = _settings.BubbleTop;
        }
        else
        {
            left = regionLeft;
            top = regionBottom + Gap;

            Native.RECT work = Native.GetWorkArea(left, top);

            if (top + height > work.Bottom - Edge)
            {
                top = regionTop - height - Gap;
            }

            if (top < work.Top + Edge)
            {
                top = work.Top + Edge;
            }

            if (left + width > work.Right - Edge)
            {
                left = work.Right - width - Edge;
            }

            if (left < work.Left + Edge)
            {
                left = work.Left + Edge;
            }
        }

        Native.SetWindowPos(handle, Native.HWND_TOPMOST, left, top, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }
}
