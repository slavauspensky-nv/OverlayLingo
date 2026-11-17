using System;
using System.Collections.Generic;
using System.Linq;

namespace WordPeek;

internal sealed class QuizQuestion
{
    public string Prompt = "";
    public string Answer = "";
    public List<string> Options = new();
}

/// <summary>Собирает вопросы в духе Anki: слово на карточке и четыре варианта на одном языке.</summary>
internal static class QuizBuilder
{
    private static readonly Random Dice = new();

    public static bool HasCyrillic(string text) => text.Any(c => c is >= 'А' and <= 'я' or 'Ё' or 'ё');

    public static QuizQuestion? Build(IReadOnlyList<NoteEntry> entries, string? avoidPrompt = null)
    {
        if (entries.Count == 0)
        {
            return null;
        }

        var pool = entries
            .Where(e => avoidPrompt is null || !string.Equals(e.Word, avoidPrompt, StringComparison.Ordinal))
            .ToList();

        if (pool.Count == 0)
        {
            pool = entries.ToList();
        }

        NoteEntry target = pool[Dice.Next(pool.Count)];
        bool answerCyrillic = HasCyrillic(target.Translation);

        var options = new List<string> { target.Translation };

        // Ложные варианты — только слова того же языка, что и верный ответ.
        var candidates = entries
            .Where(e => HasCyrillic(e.Translation) == answerCyrillic)
            .Select(e => e.Translation)
            .Where(t => !string.Equals(t, target.Translation, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(_ => Dice.Next())
            .ToList();

        foreach (string candidate in candidates)
        {
            if (options.Count >= 4)
            {
                break;
            }

            options.Add(candidate);
        }

        // Если одноязычных слов мало — добираем похожими искажениями на том же алфавите.
        while (options.Count < 4)
        {
            options.Add(Distort(target.Translation, answerCyrillic, options));
        }

        Shuffle(options);

        return new QuizQuestion
        {
            Prompt = target.Word,
            Answer = target.Translation,
            Options = options
        };
    }

    private static string Distort(string source, bool cyrillic, IReadOnlyList<string> taken)
    {
        char from = cyrillic ? 'а' : 'a';
        int span = cyrillic ? 32 : 26;

        for (int attempt = 0; attempt < 40; attempt++)
        {
            char[] chars = source.ToCharArray();

            if (chars.Length > 3 && Dice.Next(2) == 0)
            {
                int i = Dice.Next(chars.Length - 1);
                (chars[i], chars[i + 1]) = (chars[i + 1], chars[i]);
            }
            else if (chars.Length > 2)
            {
                int i = Dice.Next(chars.Length);
                chars[i] = (char)(from + Dice.Next(span));
            }

            string candidate = new string(chars);

            if (!taken.Contains(candidate, StringComparer.OrdinalIgnoreCase) &&
                !string.Equals(candidate, source, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return source + "*";
    }

    private static void Shuffle<T>(IList<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = Dice.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
