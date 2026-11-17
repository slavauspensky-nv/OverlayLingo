using System.IO;
using System.Runtime.InteropServices;

namespace OverlayLingo;

/// <summary>Звуки печати: только реальные записи, разложенные по номерам.</summary>
internal static class TypingSound
{
    public const string DefaultVariant = "1";

    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_FILENAME = 0x00020000;

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string? name, IntPtr module, uint flags);

    public static readonly (string Id, string Title)[] Variants =
    {
        ("1", "1"),
        ("2", "2"),
        ("3", "3"),
        ("4", "4"),
        ("5", "5")
    };

    private static readonly Dictionary<string, short[][]> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Random Dice = new();
    private static WaveOutMixer? _mixer;
    private static int _played;

    public static string TitleOf(string id)
    {
        foreach (var variant in Variants)
        {
            if (string.Equals(variant.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return variant.Title;
            }
        }

        return Variants[0].Title;
    }

    public static void Play(string id, double volume = 0.6)
    {
        string key = string.IsNullOrWhiteSpace(id) ? DefaultVariant : id;
        short[][]? sets;

        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out sets))
            {
                sets = BuildSamples(key);
                Cache[key] = sets;
            }
        }

        if (sets.Length == 0)
        {
            return;
        }

        short[] samples = sets[Dice.Next(sets.Length)];
        float gain = (float)Math.Clamp(volume, 0.0, 1.0);

        _mixer ??= new WaveOutMixer();

        if (_mixer.IsReady)
        {
            _mixer.Add(samples, gain);

            if (++_played <= 5)
            {
                Log.Write($"sound: набор {key}, {samples.Length} сэмплов, громкость {gain:0.00}");
            }

            return;
        }

        PlayFallback(key, samples);
    }

    /// <summary>Запасной путь, если устройство не открылось.</summary>
    private static void PlayFallback(string key, short[] samples)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "sounds", key, "click-1.wav");

            if (File.Exists(path))
            {
                PlaySound(path, IntPtr.Zero, SND_FILENAME | SND_ASYNC | SND_NODEFAULT);
            }
        }
        catch
        {
        }
    }

    /// <summary>Читает все щелчки набора из папки sounds\N.</summary>
    private static short[][] BuildSamples(string id)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "sounds", id);

        if (!Directory.Exists(directory))
        {
            Log.Write($"sound: нет папки звуков {directory}");
            return Array.Empty<short[]>();
        }

        var loaded = new List<short[]>();

        foreach (string file in Directory.GetFiles(directory, "*.wav").OrderBy(f => f))
        {
            short[]? samples = LoadWav(file);

            if (samples is not null && samples.Length > 100)
            {
                loaded.Add(samples);
            }
        }

        Log.Write($"sound: набор {id} — щелчков {loaded.Count}");
        return loaded.ToArray();
    }

    /// <summary>Читает 16-битный моно WAV.</summary>
    private static short[]? LoadWav(string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);

            if (bytes.Length < 44)
            {
                return null;
            }

            int channels = BitConverter.ToInt16(bytes, 22);
            short bits = BitConverter.ToInt16(bytes, 34);
            int dataSize = BitConverter.ToInt32(bytes, 40);

            if (bits != 16 || channels != 1 || dataSize <= 0 || 44 + dataSize > bytes.Length)
            {
                return null;
            }

            int count = dataSize / 2;
            var samples = new short[count];
            Buffer.BlockCopy(bytes, 44, samples, 0, count * 2);
            return samples;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Сколько щелчков в наборе (для диагностики).</summary>
    public static int CountOf(string id) => BuildSamples(id).Length;
}
