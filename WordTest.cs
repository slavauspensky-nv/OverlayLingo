using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace WordPeek;

/// <summary>Проверка, что клик по слову в окне перевода доходит до обработчика.</summary>
internal static class WordTest
{
    private static BubbleWindow? _window;
    private static readonly StringBuilder Log = new();

    public static void Run()
    {
        try
        {
            var settings = new AppSettings { BubbleTypeSpeed = 0, BubbleSound = false };
            _window = new BubbleWindow("Привет мир hello world", settings, 0, 0, 0, 0);

            _window.WordClicked += (word, point) =>
                Log.AppendLine($"СОБЫТИЕ WordClicked: \"{word}\" в {point.X:0},{point.Y:0}");

            _window.WordActivated += word => Log.AppendLine($"СОБЫТИЕ WordActivated: \"{word}\"");

            // Показываем окно: только в живой визуальной ветке события реально маршрутизируются.
            _window.Show();
            _window.UpdateLayout();

            var runs = new List<Run>();
            Walk(_window, runs);
            Log.AppendLine($"кликабельных слов: {runs.Count}");

            foreach (var run in runs.Take(3))
            {
                var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = ContentElement.MouseLeftButtonDownEvent
                };

                run.RaiseEvent(args);
                Log.AppendLine($"подняли событие для \"{run.Text}\"");
            }
        }
        catch (Exception ex)
        {
            Log.AppendLine("ошибка: " + ex);
        }

        try
        {
            _window?.Close();
        }
        catch
        {
        }

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "wordtest.log"), Log.ToString());
    }

    private static void Walk(DependencyObject node, List<Run> runs)
    {
        if (node is TextBlock text)
        {
            foreach (var inline in text.Inlines)
            {
                if (inline is Run run && run.Text.Trim().Length > 1 && run.Cursor == Cursors.Hand)
                {
                    runs.Add(run);
                }
            }
        }

        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);

        for (int i = 0; i < count; i++)
        {
            Walk(System.Windows.Media.VisualTreeHelper.GetChild(node, i), runs);
        }
    }
}
