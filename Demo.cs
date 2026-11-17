using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WordPeek;

/// <summary>Показывает плашку с образцом текста и сохраняет её внешний вид в overlay.png.</summary>
internal static class Demo
{
    public static void Run()
    {
        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };

        app.Startup += (_, _) =>
        {
            var overlay = new OverlayWindow();
            overlay.ShowTranslation(
                "Привет! Это проверка плашки перевода — так она будет выглядеть под строкой ввода.",
                400,
                400,
                20);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();

                try
                {
                    string path = Path.Combine(AppContext.BaseDirectory, "overlay.png");
                    overlay.SavePreview(path);
                    Log.Write("demo saved " + path);

                    SaveSettingsPreview(Path.Combine(AppContext.BaseDirectory, "settings.png"));
                    SaveAboutPreview(Path.Combine(AppContext.BaseDirectory, "about.png"));
                    SaveBubblePreview(Path.Combine(AppContext.BaseDirectory, "bubble.png"));
                    SaveBubbleWidePreview(Path.Combine(AppContext.BaseDirectory, "bubble-wide.png"));
                    SaveTrainingPreview(Path.Combine(AppContext.BaseDirectory, "training.png"));
                    SaveTipPreview(Path.Combine(AppContext.BaseDirectory, "tip.png"));
                }
                catch (Exception ex)
                {
                    Log.Write("demo error: " + ex);
                }

                app.Shutdown();
            };
            timer.Start();
        };

        app.Run();
    }

    private static void SaveBubblePreview(string path)
    {
        var previewSettings = new AppSettings { BubbleTypeSpeed = 0 };

        var window = new BubbleWindow(
            "Привет! Это облачко с переводом — так оно будет выглядеть рядом с выделенным текстом.",
            previewSettings, 0, 0, 0, 0);

        if (window.Content is not FrameworkElement content)
        {
            return;
        }

        content.Measure(new Size(460, double.PositiveInfinity));
        content.Arrange(new Rect(0, 0, content.DesiredSize.Width, content.DesiredSize.Height));

        int pixelWidth = (int)Math.Ceiling(content.DesiredSize.Width);
        int pixelHeight = (int)Math.Ceiling(content.DesiredSize.Height);
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return;
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Окно перевода с заданным пользователем размером — проверка переноса строк.</summary>
    private static void SaveBubbleWidePreview(string path)
    {
        var wideSettings = new AppSettings
        {
            BubbleTypeSpeed = 0,
            BubbleWidth = 700,
            BubbleHeight = 280
        };

        var window = new BubbleWindow(
            "Привет! Это облачко с переводом — так оно будет выглядеть рядом с выделенным текстом.",
            wideSettings, 0, 0, 0, 0);

        if (window.Content is not FrameworkElement content)
        {
            return;
        }

        content.Measure(new Size(700, 280));
        content.Arrange(new Rect(0, 0, 700, 280));

        var bitmap = new RenderTargetBitmap(700, 280, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Окно обучения на тестовом наборе слов.</summary>
    private static void SaveTrainingPreview(string path)
    {
        try
        {
            string sample = Path.Combine(AppContext.BaseDirectory, "quiz-sample.txt");

            File.WriteAllText(sample,
                "17 ноября 2026\r\ncat — кот\r\ndog — собака\r\nhouse — дом\r\ntree — дерево\r\nwindow — окно\r\n",
                new System.Text.UTF8Encoding(true));

            var window = new TrainingWindow(sample);

            if (window.Content is not FrameworkElement content)
            {
                return;
            }

            content.Measure(new Size(560, 560));
            content.Arrange(new Rect(0, 0, 560, 560));
            content.UpdateLayout();

            var bitmap = new RenderTargetBitmap(560, 560, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var stream = File.Create(path);
            encoder.Save(stream);
        }
        catch
        {
        }
    }

    private static void SaveTipPreview(string path)
    {
        var settings = new AppSettings
        {
            BubbleBackground = "#FFB74D",
            BubbleFontColor = "#000000",
            BubbleFontSize = 26
        };

        var window = new WordTipWindow("кот", settings);

        if (window.Content is not FrameworkElement content)
        {
            return;
        }

        content.Measure(new Size(360, double.PositiveInfinity));
        content.Arrange(new Rect(0, 0, content.DesiredSize.Width, content.DesiredSize.Height));

        int pixelWidth = (int)Math.Ceiling(content.DesiredSize.Width);
        int pixelHeight = (int)Math.Ceiling(content.DesiredSize.Height);
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return;
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void SaveAboutPreview(string path)
    {
        var window = new AboutWindow();
        if (window.Content is not FrameworkElement content)
        {
            return;
        }

        const double width = 440;
        content.Measure(new Size(width, double.PositiveInfinity));
        content.Arrange(new Rect(0, 0, width, content.DesiredSize.Height));

        int pixelWidth = (int)Math.Ceiling(width);
        int pixelHeight = (int)Math.Ceiling(content.DesiredSize.Height);
        if (pixelHeight <= 0)
        {
            return;
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void SaveSettingsPreview(string path)
    {
        var window = new SettingsWindow(new AppSettings());
        if (window.Content is not FrameworkElement content)
        {
            return;
        }

        const double width = 470;
        content.Measure(new Size(width, double.PositiveInfinity));
        content.Arrange(new Rect(0, 0, width, content.DesiredSize.Height));

        int pixelWidth = (int)Math.Ceiling(width);
        int pixelHeight = (int)Math.Ceiling(content.DesiredSize.Height);
        if (pixelHeight <= 0)
        {
            return;
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
