using System;
using System.IO;
using System.Text;

namespace OverlayLingo;

/// <summary>Проверка наборов звуков: сколько щелчков нашлось в каждом.</summary>
internal static class SoundTest
{
    public static void Run()
    {
        var log = new StringBuilder();

        foreach (var variant in TypingSound.Variants)
        {
            int count = TypingSound.CountOf(variant.Id);
            log.AppendLine($"набор {variant.Id}: щелчков {count}");

            if (count > 0)
            {
                TypingSound.Play(variant.Id, 0.6);
                System.Threading.Thread.Sleep(200);
            }
        }

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "soundtest.log"), log.ToString());
    }
}
