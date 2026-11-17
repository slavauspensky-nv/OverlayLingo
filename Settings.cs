using System.IO;
using System.Text.Json;

namespace WordPeek;

internal sealed class AppSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Ctrl + Alt по умолчанию.</summary>
    public uint HotkeyModifiers { get; set; } = 3;

    /// <summary>Виртуальный код клавиши T.</summary>
    public uint HotkeyKey { get; set; } = 0x54;

    /// <summary>Вторая комбинация: повторно показать последний перевод. По умолчанию Ctrl + Alt + R.</summary>
    public uint ReopenHotkeyModifiers { get; set; } = 3;

    public uint ReopenHotkeyKey { get; set; } = 0x52;

    public string Provider { get; set; } = "google";

    public string DeeplKey { get; set; } = "";

    public int DebounceMs { get; set; } = 130;

    public bool StartWithWindows { get; set; }

    /// <summary>Сообщать системе о «экранном дикторе», чтобы Chromium-приложения отдавали текст.</summary>
    public bool BrowserAccessibility { get; set; } = true;

    /// <summary>Запасной пассивный перехват клавиатуры для приложений, скрытых от UIA.</summary>
    public bool KeyboardFallback { get; set; } = true;

    /// <summary>Язык текста на скриншоте: auto, en или ru.</summary>
    public string ScreenshotSource { get; set; } = "auto";

    /// <summary>Оформление окна перевода.</summary>
    public string BubbleFont { get; set; } = "Press Start 2P";

    /// <summary>Печатать текст по буквам (эффект машинки).</summary>
    public bool BubbleTyping { get; set; } = true;

    /// <summary>Скорость «печати» текста, символов в секунду. 0 — без анимации.</summary>
    public int BubbleTypeSpeed { get; set; } = 45;

    /// <summary>Звук печати при появлении символов.</summary>
    public bool BubbleSound { get; set; } = true;

    /// <summary>Вариант звука: typewriter, mechanical, laptop, retro, membrane.</summary>
    public string BubbleSoundVariant { get; set; } = TypingSound.DefaultVariant;

    /// <summary>Громкость звука печати, 0..1.</summary>
    public double BubbleSoundVolume { get; set; } = 0.6;

    public double BubbleFontSize { get; set; } = 16;

    public string BubbleFontColor { get; set; } = "#FFFFFF";

    public string BubbleBackground { get; set; } = "#1B1D21";

    public double BubbleOpacity { get; set; } = 0.95;

    /// <summary>Запомненная позиция окна перевода; -1 — вставать рядом с выделением.</summary>
    public int BubbleLeft { get; set; } = -1;

    public int BubbleTop { get; set; } = -1;

    /// <summary>Размер окна перевода, заданный пользователем; 0 — по содержимому.</summary>
    public double BubbleWidth { get; set; }

    public double BubbleHeight { get; set; }

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("settings load: " + ex.Message);
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Log.Write("settings save: " + ex.Message);
        }
    }
}
