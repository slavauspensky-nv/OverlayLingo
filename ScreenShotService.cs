using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.Json;

namespace OverlayLingo;

internal sealed class OcrLine
{
    public string Text = "";
    public int X;
    public int Y;
    public int W;
    public int H;
}

internal sealed class OcrResult
{
    public int Width;
    public int Height;
    public List<OcrLine> Lines = new();
    public bool Cyrillic;
}

/// <summary>
/// Снимок области экрана, распознавание встроенным OCR Windows и сборка картинки с переводом.
/// </summary>
internal static class ScreenShotService
{
    /// <summary>Снимает прямоугольник экрана в физических пикселях.</summary>
    public static Bitmap Capture(int x, int y, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    /// <summary>Распознаёт текст: запускает встроенный OCR Windows через PowerShell.</summary>
    public static OcrResult? Recognize(Bitmap screenshot, string? forceLanguage = null)
    {
        OcrResult? best = null;
        double bestScore = 0;

        foreach (double factor in new[] { 1.0, 2.0, 3.0 })
        {
            Bitmap? prepared = factor <= 1.0 ? screenshot : Upscale(screenshot, factor);

            try
            {
                OcrResult? attempt = RecognizeOnce(prepared!, factor, forceLanguage);
                double score = Score(attempt);

                if (score > bestScore)
                {
                    best = attempt;
                    bestScore = score;
                }

                // Хороший результат — дальше увеличивать смысла нет.
                if (score >= 25)
                {
                    break;
                }
            }
            finally
            {
                if (prepared is not null && !ReferenceEquals(prepared, screenshot))
                {
                    prepared.Dispose();
                }
            }
        }

        if (best is not null)
        {
            Log.Write($"screenshot: лучший вариант дал {bestScore:0} букв, кириллица={best.Cyrillic}");
        }

        return best;
    }

    private static double Score(OcrResult? result)
    {
        if (result is null || result.Lines.Count == 0)
        {
            return 0;
        }

        return result.Lines.Sum(line => line.Text.Count(char.IsLetter));
    }

    private static Bitmap Upscale(Bitmap source, double factor)
    {
        int width = (int)Math.Round(source.Width * factor);
        int height = (int)Math.Round(source.Height * factor);

        var enlarged = new Bitmap(width, height);

        using (var graphics = Graphics.FromImage(enlarged))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.DrawImage(source, 0, 0, width, height);
        }

        return enlarged;
    }

    private static OcrResult? RecognizeOnce(Bitmap screenshot, double scale, string? forceLanguage = null)
    {
        string script = Path.Combine(AppContext.BaseDirectory, "ocr.ps1");
        if (!File.Exists(script))
        {
            Log.Write("screenshot: не найден ocr.ps1");
            return null;
        }

        // Сохраняем рядом с программой: эта папка точно доступна (в неё же пишется лог).
        // Уникальное имя — чтобы не поймать блокировку от предыдущего запуска.
        string imagePath = Path.Combine(AppContext.BaseDirectory, $"shot-tmp-{Guid.NewGuid():N}.png");

        try
        {
            screenshot.Save(imagePath, ImageFormat.Png);
        }
        catch (Exception ex)
        {
            Log.Write("screenshot: не смог сохранить снимок " + ex.Message);
            return null;
        }

        try
        {
            string languages = string.IsNullOrWhiteSpace(forceLanguage) ? "en-US,ru" : forceLanguage;

            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Path \"{imagePath}\" -Languages \"{languages}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            string json = process.StandardOutput.ReadToEnd();
            process.WaitForExit(15000);

            if (string.IsNullOrWhiteSpace(json))
            {
                Log.Write("screenshot: пустой ответ OCR");
                return null;
            }

            OcrResult? parsed = ParseOcr(json);

            // Координаты приводим обратно к исходному масштабу.
            if (parsed is not null && Math.Abs(scale - 1.0) > 0.01)
            {
                foreach (var line in parsed.Lines)
                {
                    line.X = (int)Math.Round(line.X / scale);
                    line.Y = (int)Math.Round(line.Y / scale);
                    line.W = (int)Math.Round(line.W / scale);
                    line.H = (int)Math.Round(line.H / scale);
                }
            }

            return parsed;
        }
        catch (Exception ex)
        {
            Log.Write("screenshot: OCR упал " + ex.Message);
            return null;
        }
        finally
        {
            try
            {
                File.Delete(imagePath);
            }
            catch
            {
            }
        }
    }

    private static OcrResult? ParseOcr(string json)
    {
        try
        {
            // Страховка: управляющие символы внутри строк делают JSON невалидным.
            var clean = new StringBuilder(json.Length);

            foreach (char ch in json)
            {
                clean.Append(ch < 0x20 ? ' ' : ch);
            }

            using var document = JsonDocument.Parse(clean.ToString().Trim());
            var root = document.RootElement;

            if (root.TryGetProperty("error", out _))
            {
                Log.Write("screenshot: OCR сообщил ошибку");
                return null;
            }

            var result = new OcrResult
            {
                Width = root.GetProperty("width").GetInt32(),
                Height = root.GetProperty("height").GetInt32()
            };

            if (!root.TryGetProperty("results", out var results))
            {
                return null;
            }

            var candidates = new List<(List<OcrLine> Lines, double Foreign)>();

            foreach (var language in results.EnumerateObject())
            {
                var lines = new List<OcrLine>();

                foreach (var item in language.Value.EnumerateArray())
                {
                    string text = item.GetProperty("text").GetString() ?? "";
                    if (text.Trim().Length < 2)
                    {
                        continue;
                    }

                    lines.Add(new OcrLine
                    {
                        Text = text.Trim(),
                        X = item.GetProperty("x").GetInt32(),
                        Y = item.GetProperty("y").GetInt32(),
                        W = item.GetProperty("w").GetInt32(),
                        H = item.GetProperty("h").GetInt32()
                    });
                }

                if (lines.Count == 0)
                {
                    continue;
                }

                bool cyrillic = language.Name.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
                candidates.Add((lines, ForeignRatio(lines, cyrillic)));
            }

            if (candidates.Count == 0)
            {
                Log.Write("ocr: ни один язык не дал строк");
                return null;
            }

            Log.Write($"ocr: вариантов {candidates.Count}: " + string.Join(" | ",
                candidates.Select(c => $"\"{string.Join(" / ", c.Lines.Select(l => l.Text))}\" (чужих {c.Foreign:0.00})")));

            // Решаем по доле кириллицы в русской модели: если она прочитала текст
            // в основном кириллицей — значит текст русский. Английская модель
            // «читает» русский латиницей, и по ней это не определить.
            List<OcrLine>? russianLines = null;
            foreach (var candidate in candidates)
            {
                if (candidate.Lines.Any(l => l.Text.Any(IsCyrillic)))
                {
                    russianLines = candidate.Lines;
                    break;
                }
            }

            double cyrillicShare = 0;
            int cyrillicLetters = 0;

            if (russianLines is not null)
            {
                int totalLetters = 0;

                foreach (var line in russianLines)
                {
                    foreach (char ch in line.Text)
                    {
                        if (!char.IsLetter(ch))
                        {
                            continue;
                        }

                        totalLetters++;
                        if (IsCyrillic(ch))
                        {
                            cyrillicLetters++;
                        }
                    }
                }

                cyrillicShare = totalLetters == 0 ? 0 : (double)cyrillicLetters / totalLetters;
            }

            bool isRussian = cyrillicShare >= 0.5 && cyrillicLetters >= 3;

            List<OcrLine> chosen = isRussian && russianLines is not null
                ? russianLines
                : candidates.OrderBy(c => c.Foreign).First().Lines;

            result.Lines = chosen;
            result.Cyrillic = isRussian;

            // Отбрасываем строки с большим количеством мусора.
            result.Lines = result.Lines
                .Where(l => LetterRatio(l.Text, result.Cyrillic) >= 0.6)
                .Where(l => l.Text.Count(char.IsLetter) >= 3)
                .ToList();

            return result;
        }
        catch (Exception ex)
        {
            Log.Write("screenshot: разбор OCR " + ex.Message);
            return null;
        }
    }

    private static bool IsCyrillic(char ch)
    {
        return ch is >= 'А' and <= 'я' || ch is 'Ё' or 'ё';
    }

    private static double ForeignRatio(IEnumerable<OcrLine> lines, bool cyrillic)
    {
        int foreign = 0;
        int total = 0;

        foreach (var line in lines)
        {
            foreach (char ch in line.Text)
            {
                if (!char.IsLetter(ch))
                {
                    continue;
                }

                total++;
                if (IsCyrillic(ch) != cyrillic)
                {
                    foreign++;
                }
            }
        }

        return total == 0 ? 1.0 : (double)foreign / total;
    }

    private static double LetterRatio(string text, bool cyrillic)
    {
        int letters = 0;
        int matching = 0;

        foreach (char ch in text)
        {
            if (!char.IsLetter(ch))
            {
                continue;
            }

            letters++;
            if (IsCyrillic(ch) == cyrillic)
            {
                matching++;
            }
        }

        return letters == 0 ? 0.0 : (double)matching / letters;
    }

    /// <summary>Рисует перевод поверх исходных строк и возвращает готовую картинку.</summary>
    public static Bitmap Compose(Bitmap source, IReadOnlyList<(OcrLine Line, string Translation)> items)
    {
        var result = new Bitmap(source);

        using var graphics = Graphics.FromImage(result);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        foreach (var (line, translation) in items)
        {
            if (string.IsNullOrWhiteSpace(translation))
            {
                continue;
            }

            var box = new Rectangle(
                Math.Max(line.X - 3, 0),
                Math.Max(line.Y - 2, 0),
                Math.Min(line.W + 6, result.Width - Math.Max(line.X - 3, 0)),
                Math.Min(line.H + 4, result.Height - Math.Max(line.Y - 2, 0)));

            if (box.Width < 10 || box.Height < 8)
            {
                continue;
            }

            Color background = SampleBackground(source, box);
            bool dark = background.GetBrightness() < 0.5;
            Color foreground = dark ? Color.White : Color.Black;

            Font font = FitFont(graphics, translation, box);
            SizeF measured = graphics.MeasureString(translation, font);

            // Перевод часто длиннее оригинала — разрешаем выйти вправо,
            // если там свободно, вместо обрезки многоточием.
            int available = result.Width - box.X - 2;
            int width = Math.Min(Math.Max(box.Width, (int)Math.Ceiling(measured.Width) + 6), available);

            var plate = new Rectangle(box.X, box.Y, width, box.Height);

            using (var brush = new SolidBrush(background))
            {
                graphics.FillRectangle(brush, plate);
            }

            using (font)
            using (var brush = new SolidBrush(foreground))
            using (var format = new StringFormat
            {
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.None
            })
            {
                graphics.DrawString(translation, font, brush,
                    new RectangleF(plate.X + 2, plate.Y + 1, plate.Width - 4, plate.Height - 2), format);
            }
        }

        return result;
    }

    /// <summary>
    /// Собирает текст с сохранением структуры: строки, закончившиеся знаком конца
    /// предложения или заметно более короткие, остаются отдельными;
    /// обрывающиеся по ширине склеиваются через пробел.
    /// </summary>
    public static string BuildStructuredText(IReadOnlyList<OcrLine> lines)
    {
        if (lines.Count == 0)
        {
            return "";
        }

        if (lines.Count == 1)
        {
            return lines[0].Text.Trim();
        }

        int maxRight = lines.Max(line => line.X + line.W);
        int averageHeight = (int)Math.Max(lines.Average(line => line.H), 10);

        var builder = new StringBuilder();

        for (int i = 0; i < lines.Count; i++)
        {
            string current = lines[i].Text.Trim();
            builder.Append(current);

            if (i == lines.Count - 1)
            {
                break;
            }

            bool endsSentence = current.Length > 0 && ".!?;:…".IndexOf(current[^1]) >= 0;
            int right = lines[i].X + lines[i].W;
            bool reachesEdge = right >= maxRight - averageHeight;

            builder.Append(reachesEdge && !endsSentence ? " " : "\n");
        }

        return builder.ToString().Trim();
    }

    private static Font FitFont(Graphics graphics, string text, Rectangle box)
    {
        float size = Math.Max(box.Height * 0.68f, 7f);
        const float minSize = 7f;

        while (size >= minSize)
        {
            var font = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel);

            try
            {
                SizeF measured = graphics.MeasureString(text, font);
                if (measured.Width <= box.Width - 6)
                {
                    return font;
                }
            }
            catch
            {
            }

            font.Dispose();
            size -= 0.5f;
        }

        // Не влезло даже мелким — оставляем минимальный, текст выйдет правее.
        return new Font("Segoe UI", minSize, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    private static Color SampleBackground(Bitmap source, Rectangle box)
    {
        try
        {
            int[] xs = { box.Left - 2, box.Right + 2, box.Left + box.Width / 2 };

            foreach (int x in xs)
            {
                if (x >= 0 && x < source.Width && box.Top - 1 >= 0)
                {
                    return source.GetPixel(x, box.Top - 1);
                }
            }

            int y = Math.Min(box.Bottom + 2, source.Height - 1);
            if (y >= 0)
            {
                return source.GetPixel(box.Left, y);
            }
        }
        catch
        {
        }

        return Color.White;
    }
}
