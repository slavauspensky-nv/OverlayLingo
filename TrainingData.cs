using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace WordPeek;

internal sealed class NoteEntry
{
    public string Word = "";
    public string Translation = "";
    public string Day = "";
}

/// <summary>Читает файл заметок WordPeek.txt и раскладывает слова по дням.</summary>
internal static class TrainingData
{
    private static readonly Regex DayPattern = new(@"^\d{1,2}\s+\S+\s+\d{4}$", RegexOptions.Compiled);

    public static string NotesPath()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
        {
            desktop = AppContext.BaseDirectory;
        }

        return Path.Combine(desktop, "WordPeek.txt");
    }

    public static List<NoteEntry> Load(string? path = null)
    {
        var entries = new List<NoteEntry>();
        path ??= NotesPath();

        if (!File.Exists(path))
        {
            return entries;
        }

        string day = "";

        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            if (DayPattern.IsMatch(line))
            {
                day = line;
                continue;
            }

            int separator = line.IndexOf('—');

            if (separator <= 0)
            {
                continue;
            }

            string word = line[..separator].Trim();
            string translation = line[(separator + 1)..].Trim();

            if (word.Length == 0 || translation.Length == 0)
            {
                continue;
            }

            entries.Add(new NoteEntry { Word = word, Translation = translation, Day = day });
        }

        return entries;
    }
}
