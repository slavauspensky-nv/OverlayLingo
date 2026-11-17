using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace OverlayLingo;

/// <summary>Выбор цвета из палитры.</summary>
internal sealed class ColorPicker : Grid
{
    private static readonly string[] Palette =
    {
        "#FFFFFF", "#F2F2F2", "#D9D9D9", "#BFBFBF", "#8C8C8C", "#595959", "#333333", "#000000",
        "#FFE4E1", "#FFCDD2", "#FF8A80", "#FF5252", "#D50000", "#B71C1C", "#8E0000", "#4A0000",
        "#FFF3E0", "#FFE0B2", "#FFB74D", "#FF9800", "#F57C00", "#E65100", "#BF360C", "#7F2A00",
        "#FFFDE7", "#FFF9C4", "#FFF176", "#FFEB3B", "#FBC02D", "#F9A825", "#F57F17", "#7A4F00",
        "#F1F8E9", "#DCEDC8", "#AED581", "#8BC34A", "#689F38", "#33691E", "#1B5E20", "#0B3D0B",
        "#E0F7FA", "#B2EBF2", "#4DD0E1", "#00BCD4", "#0097A7", "#006064", "#004D40", "#002620",
        "#E3F2FD", "#BBDEFB", "#64B5F6", "#2196F3", "#1976D2", "#0D47A1", "#082E66", "#04142E",
        "#EDE7F6", "#D1C4E9", "#9575CD", "#673AB7", "#512DA8", "#311B92", "#1A0D4D", "#0A0526"
    };

    private readonly Border _swatch;
    private readonly TextBlock _hex;
    private readonly Popup _popup;

    public ColorPicker(Color initial)
    {
        Selected = initial;

        _swatch = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0xBC, 0xC4)),
            Background = new SolidColorBrush(initial),
            VerticalAlignment = VerticalAlignment.Center
        };

        _hex = new TextBlock
        {
            Text = Hex(initial),
            FontSize = 12,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(_swatch);
        content.Children.Add(_hex);

        var button = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 4, 10, 4),
            MinWidth = 130,
            Cursor = Cursors.Hand
        };

        _popup = new Popup
        {
            PlacementTarget = button,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true
        };

        button.Click += (_, _) => _popup.IsOpen = !_popup.IsOpen;

        var grid = new WrapPanel { Width = 8 * 26 + 16, ItemWidth = 26, ItemHeight = 26 };

        foreach (string code in Palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(code);

            var cell = new Border
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)),
                Cursor = Cursors.Hand,
                ToolTip = code
            };

            cell.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                Selected = color;
                _swatch.Background = new SolidColorBrush(color);
                _hex.Text = Hex(color);
                _popup.IsOpen = false;
                Changed?.Invoke(color);
            };

            grid.Children.Add(cell);
        }

        var frame = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xCC, 0xD4)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6),
            Child = grid,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 12,
                ShadowDepth = 2,
                Opacity = 0.25,
                Direction = 270
            }
        };

        _popup.Child = frame;

        Children.Add(button);
    }

    public Color Selected { get; private set; }

    public event Action<Color>? Changed;

    public static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
