using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfImage = System.Windows.Controls.Image;
using WpfColor = System.Windows.Media.Color;

namespace OverlayLingo;

internal sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = "О программе";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(WpfColor.FromRgb(0xF7, 0xF8, 0xFA));

        var root = new StackPanel { Margin = new Thickness(24) };

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        WpfImage? icon = TryLoadIcon();
        if (icon is not null)
        {
            icon.Margin = new Thickness(0, 0, 16, 0);
            header.Children.Add(icon);
        }

        var titles = new StackPanel();
        titles.Children.Add(new TextBlock
        {
            Text = "OverlayLingo",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x22, 0x28))
        });
        titles.Children.Add(new TextBlock
        {
            Text = "Живой перевод текста из активного поля ввода",
            FontSize = 12.5,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x6B, 0x71, 0x7A)),
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        header.Children.Add(titles);
        root.Children.Add(header);

        var grid = new Grid { Margin = new Thickness(0, 22, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddRow(grid, 0, "Разработчики", "Vladislav Uspensky", true);
        AddRow(grid, 1, "", "Vyacheslav Uspensky", true);
        AddRow(grid, 2, "Версия", Version(), false);
        AddRow(grid, 3, "Сборка", BuildDate(), false);
        AddRow(grid, 4, "Горячая клавиша", "Ctrl + Alt + T — включить/выключить", false);
        AddRow(grid, 5, "Снимок с переводом", "Ctrl + Alt + S", false);
        AddRow(grid, 6, "Перевод", "Google", false);

        root.Children.Add(grid);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 24, 0, 0)
        };

        var close = new Button
        {
            Content = "Закрыть",
            Padding = new Thickness(22, 7, 22, 7),
            FontSize = 13,
            IsDefault = true
        };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);
        root.Children.Add(buttons);

        Content = root;
    }

    private static void AddRow(Grid grid, int row, string caption, string value, bool emphasize)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = caption,
            FontSize = 12,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x7A, 0x80, 0x8A)),
            Margin = new Thickness(0, 4, 8, 4)
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var text = new TextBlock
        {
            Text = value,
            FontSize = emphasize ? 13.5 : 12.5,
            FontWeight = emphasize ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x22, 0x28)),
            Margin = new Thickness(0, 4, 0, 4),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
    }

    private static string Version()
    {
        try
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        }
        catch
        {
            return "1.0.0";
        }
    }

    private static string BuildDate()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            return exe is not null && File.Exists(exe)
                ? File.GetLastWriteTime(exe).ToString("dd.MM.yyyy HH:mm")
                : "—";
        }
        catch
        {
            return "—";
        }
    }

    private static WpfImage? TryLoadIcon()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                return null;
            }

            using System.Drawing.Icon icon =
                System.Drawing.Icon.ExtractAssociatedIcon(exe) ?? System.Drawing.SystemIcons.Application;

            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            return new WpfImage { Source = source, Width = 56, Height = 56 };
        }
        catch
        {
            return null;
        }
    }
}
