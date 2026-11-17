using System.IO;
using System.Text;

namespace WordPeek;

/// <summary>Прогоняет реальный сервис перевода и пишет результат в translate.log.</summary>
internal static class TranslateTest
{
    public static void Run(string? first)
    {
        var settings = AppSettings.Load();
        var translator = new Translator(settings);
        var log = new StringBuilder();

        var phrases = new List<string>();
        if (!string.IsNullOrWhiteSpace(first))
        {
            phrases.Add(first);
        }

        phrases.AddRange(new[]
        {
            "Hello, how are you today?",
            "Привет, как дела сегодня?",
            "LFG need tank and healer",
            "Did u now"
        });

        foreach (string phrase in phrases)
        {
            try
            {
                string? result = translator.TranslateAsync(phrase, CancellationToken.None)
                    .GetAwaiter().GetResult();
                log.AppendLine($"\"{phrase}\" -> \"{result ?? "(null)"}\"");
                Log.Write($"translate: {phrase} -> {result}");
            }
            catch (Exception ex)
            {
                log.AppendLine($"\"{phrase}\" -> ERROR {ex.Message}");
                Log.Write("translate error: " + ex);
            }
        }

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "translate.log"), log.ToString());
    }
}
