using System.IO;
using System.Text;

namespace WordPeek;

/// <summary>
/// Проверяет разбор ответов Google на реальных образцах, без обращения к сети.
/// Результат — в parsetest.log.
/// </summary>
internal static class ParseTest
{
    public static void Run()
    {
        var log = new StringBuilder();
        int passed = 0;
        int failed = 0;

        void Check(string name, string json, string? expected, string? actual)
        {
            bool ok = expected == actual;
            if (ok)
            {
                passed++;
            }
            else
            {
                failed++;
            }

            log.AppendLine($"{(ok ? "PASS" : "FAIL")} {name}: ожидали=\"{expected}\" получили=\"{actual}\"");
        }

        const string singleRu =
            "[[[\"Как дела?\",\"How do you do?\",null,null,10]],null,\"en\",null,null,null,1,[],[[\"en\"],null,[1],[\"en\"]]]";
        Check("single en→ru", singleRu, "Как дела?", Translator.ParseSingle(singleRu));

        const string singleEn =
            "[[[\"Hi, how are you?\",\"Привет, как дела?\",null,null,10]],null,\"ru\",null,null,null,1,[],[[\"ru\"],null,[1],[\"ru\"]]]";
        Check("single ru→en", singleEn, "Hi, how are you?", Translator.ParseSingle(singleEn));

        const string partial =
            "[[[\"Как\",\"How do\",null,null,3,null,null,[[]],[[[\"8e6adaf1f9ae06bcb9663531e5521abb\",\"en_ru_2023q1.md\"]]]]],null,\"en\",null,null,null,1,[],[[\"en\"],null,[1],[\"en\"]]]";
        Check("недопечатанное слово", partial, "Как", Translator.ParseSingle(partial));

        const string multi =
            "[[[\"Привет. \",\"Hello. \",null,null,10],[\"Как дела?\",\"How are you?\",null,null,10]],null,\"en\"]";
        Check("несколько сегментов", multi, "Привет. Как дела?", Translator.ParseSingle(multi));

        const string clients5 = "[[\"Как дела?\",\"en\"]]";
        Check("clients5", clients5, "Как дела?", Translator.ParseClients5(clients5));

        const string broken = "<html>Sorry...</html>";
        Check("мусор вместо JSON", broken, null, Translator.ParseSingle(broken));

        Check("пустой ответ", "[]", null, Translator.ParseSingle("[]"));

        log.AppendLine($"итого: passed={passed} failed={failed}");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "parsetest.log"), log.ToString());
    }
}
