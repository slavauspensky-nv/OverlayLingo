using System;
using System.IO;
using System.Linq;
using System.Text;

namespace OverlayLingo;

/// <summary>Проверка разбора заметок и сборки вопросов для обучения.</summary>
internal static class QuizTest
{
    public static void Run()
    {
        var log = new StringBuilder();

        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "quiz-sample.txt");

            File.WriteAllText(path,
                "17 ноября 2026\r\n" +
                "cat — кот\r\n" +
                "dog — собака\r\n" +
                "house — дом\r\n" +
                "tree — дерево\r\n" +
                "\r\n" +
                "18 ноября 2026\r\n" +
                "window — окно\r\n" +
                "river — река\r\n",
                new UTF8Encoding(true));

            var all = TrainingData.Load(path);
            log.AppendLine($"прочитано слов: {all.Count}");
            log.AppendLine("дни: " + string.Join(", ", all.Select(e => e.Day).Distinct()));

            var day2 = all.Where(e => e.Day.StartsWith("18")).ToList();
            log.AppendLine($"слов за 18 ноября: {day2.Count}");

            for (int i = 0; i < 5; i++)
            {
                var question = QuizBuilder.Build(all);

                if (question is null)
                {
                    log.AppendLine("вопрос не собран");
                    continue;
                }

                var distinct = question.Options.Distinct(StringComparer.Ordinal).Count();
                bool hasAnswer = question.Options.Contains(question.Answer, StringComparer.Ordinal);

                log.AppendLine(
                    $"вопрос: \"{question.Prompt}\" → верный \"{question.Answer}\"; " +
                    $"вариантов {question.Options.Count} (уникальных {distinct}), верный среди них: {hasAnswer}");
            }

            // Мало слов — проверяем, что добираются искажениями
            var tiny = all.Take(2).ToList();
            var small = QuizBuilder.Build(tiny);
            log.AppendLine(small is null
                ? "на двух словах вопрос не собрался"
                : $"на двух словах: вариантов {small.Options.Count}, верный среди них: {small.Options.Contains(small.Answer)}");
        }
        catch (Exception ex)
        {
            log.AppendLine("ошибка: " + ex);
        }

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "quiztest.log"), log.ToString());
    }
}
