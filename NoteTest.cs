using System;
using System.IO;
using System.Text;

namespace OverlayLingo;

/// <summary>Проверка разбивки заметок по датам.</summary>
internal static class NoteTest
{
    public static void Run()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "notetest.txt");

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            var day1 = new DateTime(2026, 11, 17, 10, 0, 0);
            var day2 = new DateTime(2026, 11, 18, 9, 0, 0);

            NoteWriter.Append(path, day1, "cat — кот");
            NoteWriter.Append(path, day1, "dog — собака");
            NoteWriter.Append(path, day2, "house — дом");

            string text = File.ReadAllText(path);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "notetest.log"),
                "--- файл ---" + Environment.NewLine + text.Replace("\r", "[CR]"), new UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "notetest.log"), "ошибка: " + ex);
        }
    }
}
