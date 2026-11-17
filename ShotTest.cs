using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace WordPeek;

/// <summary>Проверка снимка без сети: распознавание + отрисовка поверх картинки.</summary>
internal static class ShotTest
{
    public static void Run(string? imagePath)
    {
        var log = new StringBuilder();

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "shottest.log"), "нет картинки");
            return;
        }

        try
        {
            using var source = new Bitmap(imagePath);
            OcrResult? ocr = ScreenShotService.Recognize(source);

            if (ocr is null)
            {
                log.AppendLine("OCR не дал результата");
            }
            else
            {
                log.AppendLine($"размер={ocr.Width}x{ocr.Height} строк={ocr.Lines.Count} кириллица={ocr.Cyrillic}");

                foreach (var line in ocr.Lines)
                {
                    log.AppendLine($"  [{line.X},{line.Y} {line.W}x{line.H}] {line.Text}");
                }

                var items = new List<(OcrLine Line, string Translation)>();
                foreach (var line in ocr.Lines)
                {
                    items.Add((line, "[ПЕРЕВОД] " + line.Text.ToUpperInvariant()));
                }

                string structured = ScreenShotService.BuildStructuredText(ocr.Lines);
                log.AppendLine("структура: " + structured.Replace("\n", "\\n"));

                using var composed = ScreenShotService.Compose(source, items);
                string output = Path.Combine(AppContext.BaseDirectory, "shot-composed.png");
                composed.Save(output, ImageFormat.Png);
                log.AppendLine("сохранено: " + output);
            }
        }
        catch (Exception ex)
        {
            log.AppendLine("ошибка: " + ex);
        }

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "shottest.log"), log.ToString());
    }
}
