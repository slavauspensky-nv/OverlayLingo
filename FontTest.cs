using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WordPeek;

/// <summary>Отрисовывает образец всех встроенных шрифтов.</summary>
internal static class FontTest
{
    public static void Run()
    {
        try
        {
            var panel = new StackPanel
            {
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1A)),
                Width = 620
            };

            var log = new System.Text.StringBuilder();

            foreach (string name in FontHelper.Names)
            {
                FontFamily family = FontHelper.Resolve(name);

                var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                string uri = "нет";

                if (typeface.TryGetGlyphTypeface(out GlyphTypeface glyph))
                {
                    uri = glyph.FontUri.ToString();
                }

                bool ours = uri.Contains("/fonts/", StringComparison.OrdinalIgnoreCase);
                log.AppendLine($"{name}\t{(ours ? "OK" : "ПОДМЕНА")}\t{uri}");

                var row = new TextBlock
                {
                    Text = $"{name}: Привет Hello 123",
                    FontFamily = family,
                    FontSize = 20,
                    Foreground = Brushes.White,
                    Margin = new Thickness(10, 6, 10, 6)
                };

                panel.Children.Add(row);
            }

            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "fonttest.log"), log.ToString());

            panel.Measure(new Size(620, double.PositiveInfinity));
            panel.Arrange(new Rect(0, 0, 620, panel.DesiredSize.Height));

            int width = (int)Math.Ceiling(panel.DesiredSize.Width);
            int height = (int)Math.Ceiling(panel.DesiredSize.Height);

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(panel);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "fonts-sheet.png"));
            encoder.Save(stream);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "fonttest.log"), ex.ToString());
        }
    }
}
