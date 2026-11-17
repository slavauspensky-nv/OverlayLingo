using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OverlayLingo;

/// <summary>Файл заметок: слова сгруппированы по дням.</summary>
internal static class NoteWriter
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly Regex HeaderPattern = new(@"^\d{1,2}\s+[А-Яа-яЁё]+\s+\d{4}$", RegexOptions.Compiled);

    public static string Header(DateTime day) => day.ToString("d MMMM yyyy", Russian);

    /// <summary>Дописывает слово под заголовком текущего дня, создавая его при смене даты.</summary>
    public static void Append(string path, DateTime day, string entry)
    {
        string header = Header(day);
        string content = File.Exists(path) ? File.ReadAllText(path) : "";

        // Ищем последний заголовок дня, а не последнюю строку:
        // иначе под одним и тем же днём заголовок повторялся бы перед каждым словом.
        string lastHeader = "";
        foreach (string raw in content.Split('\n'))
        {
            string trimmed = raw.TrimEnd('\r').Trim();
            if (HeaderPattern.IsMatch(trimmed))
            {
                lastHeader = trimmed;
            }
        }

        bool needsHeader = lastHeader != header;

        var builder = new StringBuilder();

        if (content.Length > 0)
        {
            if (!content.EndsWith("\n", StringComparison.Ordinal))
            {
                builder.Append(Environment.NewLine);
            }

            if (needsHeader)
            {
                builder.Append(Environment.NewLine);
            }
        }

        if (needsHeader)
        {
            builder.Append(header).Append(Environment.NewLine);
        }

        builder.Append(entry).Append(Environment.NewLine);

        File.AppendAllText(path, builder.ToString(), new UTF8Encoding(true));
    }
}
