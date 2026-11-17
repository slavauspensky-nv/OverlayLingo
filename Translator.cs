using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace WordPeek;

internal static class LanguageDetector
{
    /// <summary>Куда переводить: если текста на кириллице больше — переводим на английский.</summary>
    public static string TargetLanguage(string text)
    {
        int cyrillic = 0;
        int latin = 0;

        foreach (char ch in text)
        {
            if (ch is >= 'А' and <= 'я' || ch is 'Ё' or 'ё')
            {
                cyrillic++;
            }
            else if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
            {
                latin++;
            }
        }

        return cyrillic > latin ? "en" : "ru";
    }
}

internal sealed class Translator
{
    private const int CacheLimit = 2000;

    /// <summary>Минимальный интервал между сетевыми запросами: иначе Google отвечает 429.</summary>
    private const int MinRequestGapMs = 260;

    private static readonly object RateGate = new();
    private static DateTime _lastRequestAt = DateTime.MinValue;
    private static DateTime _primaryBlockedUntil = DateTime.MinValue;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(7)
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly Queue<string> _cacheOrder = new();

    public Translator(AppSettings settings)
    {
        Settings = settings;
    }

    public AppSettings Settings { get; }

    public async Task<string?> TranslateAsync(string rawText, CancellationToken cancellationToken, string? forceTarget = null)
    {
        string text = rawText.Trim();
        if (text.Length < 2)
        {
            return null;
        }

        string target = forceTarget ?? LanguageDetector.TargetLanguage(text);

        // Ключ кэша включает направление: одно и то же слово переводят в обе стороны.
        string cacheKey = target + "|" + text;

        lock (_gate)
        {
            if (_cache.TryGetValue(cacheKey, out string? cached))
            {
                return cached;
            }
        }

        string? result;

        try
        {
            result = Settings.Provider == "deepl" && !string.IsNullOrWhiteSpace(Settings.DeeplKey)
                ? await TranslateDeeplAsync(text, target, cancellationToken)
                : await TranslateGoogleAsync(text, target, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Write("translate: " + ex.Message);
            return null;
        }

        if (string.IsNullOrWhiteSpace(result))
        {
            return null;
        }

        result = result.Trim();
        lock (_gate)
        {
            Store(cacheKey, result);
        }

        return result;
    }

    private static async Task<string?> TranslateGoogleAsync(string text, string target, CancellationToken cancellationToken)
    {
        string? primary = await TryGoogleSingleAsync(text, target, cancellationToken);
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary;
        }

        string? secondary = await TryGoogleClients5Async(text, target, cancellationToken);
        if (!string.IsNullOrWhiteSpace(secondary))
        {
            return secondary;
        }

        // Оба эндпоинта Google упёрлись в лимит — идём в независимый сервис.
        return await TryMyMemoryAsync(text, target, cancellationToken);
    }

    /// <summary>Запасной переводчик на случай блокировки Google по лимиту запросов.</summary>
    private static async Task<string?> TryMyMemoryAsync(string text, string target, CancellationToken cancellationToken)
    {
        try
        {
            string source = target == "ru" ? "en" : "ru";

            string url = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(text) +
                         "&langpair=" + Uri.EscapeDataString(source + "|" + target);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) WordPeek/1.0");

            using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                Log.Write("mymemory status " + (int)response.StatusCode);
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseMyMemory(json);
        }
        catch (Exception ex)
        {
            Log.Write("mymemory: " + ex.Message);
            return null;
        }
    }

    /// <summary>Разбор ответа api.mymemory.translated.net.</summary>
    internal static string? ParseMyMemory(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("responseData", out var data) ||
                !data.TryGetProperty("translatedText", out var translated))
            {
                return null;
            }

            string text = translated.GetString() ?? "";

            if (text.Length == 0 || text.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Возвращаем HTML-сущности к обычным символам.
            return System.Net.WebUtility.HtmlDecode(text).Trim();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> TryGoogleSingleAsync(string text, string target, CancellationToken cancellationToken)
    {
        // Недавно получили 429 — какое-то время идём сразу на запасной эндпоинт.
        if (DateTime.UtcNow < _primaryBlockedUntil)
        {
            return null;
        }

        await ThrottleAsync(cancellationToken);

        string url =
            "https://translate.googleapis.com/translate_a/single" +
            "?client=at&dt=t&sl=auto&tl=" + Uri.EscapeDataString(target) +
            "&q=" + Uri.EscapeDataString(text);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) WordPeek/1.0");

        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            int code = (int)response.StatusCode;
            Log.Write("google/at status " + code);

            if (code == 429)
            {
                _primaryBlockedUntil = DateTime.UtcNow.AddSeconds(12);
            }

            return null;
        }

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseSingle(json);
    }

    /// <summary>Разбор ответа translate.googleapis.com/translate_a/single.</summary>
    internal static string? ParseSingle(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            {
                return null;
            }

            var builder = new StringBuilder();
            foreach (var segment in root[0].EnumerateArray())
            {
                if (segment.ValueKind != JsonValueKind.Array || segment.GetArrayLength() == 0)
                {
                    continue;
                }

                string? piece = segment[0].GetString();
                if (!string.IsNullOrEmpty(piece))
                {
                    builder.Append(piece);
                }
            }

            string result = builder.ToString();
            return result.Length == 0 ? null : result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Разбор ответа clients5.google.com/translate_a/t.</summary>
    internal static string? ParseClients5(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            {
                return null;
            }

            var first = root[0];
            if (first.ValueKind == JsonValueKind.String)
            {
                return first.GetString();
            }

            if (first.ValueKind == JsonValueKind.Array && first.GetArrayLength() > 0)
            {
                var inner = first[0];
                if (inner.ValueKind == JsonValueKind.String)
                {
                    return inner.GetString();
                }
            }
        }
        catch
        {
        }

        return null;
    }

    /// <summary>Запасной эндпоинт Google на случай, если основной начнёт отдавать 429.</summary>
    private static async Task<string?> TryGoogleClients5Async(string text, string target, CancellationToken cancellationToken)
    {
        string url =
            "https://clients5.google.com/translate_a/t?client=dict-chrome-ex" +
            "&sl=auto&tl=" + Uri.EscapeDataString(target) +
            "&q=" + Uri.EscapeDataString(text);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) WordPeek/1.0");

        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseClients5(json);
    }

    private async Task<string?> TranslateDeeplAsync(string text, string target, CancellationToken cancellationToken)
    {
        string key = Settings.DeeplKey.Trim();
        string endpoint = key.EndsWith(":fx", StringComparison.OrdinalIgnoreCase)
            ? "https://api-free.deepl.com/v2/translate"
            : "https://api.deepl.com/v2/translate";

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["text"] = text,
            ["target_lang"] = target.ToUpperInvariant()
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        request.Headers.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key " + key);

        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        return document.RootElement
            .GetProperty("translations")[0]
            .GetProperty("text")
            .GetString();
    }

    /// <summary>Выдерживает минимальный интервал между запросами.</summary>
    private static async Task ThrottleAsync(CancellationToken cancellationToken)
    {
        TimeSpan delay;

        lock (RateGate)
        {
            DateTime earliest = _lastRequestAt.AddMilliseconds(MinRequestGapMs);
            DateTime now = DateTime.UtcNow;
            delay = earliest > now ? earliest - now : TimeSpan.Zero;
            _lastRequestAt = now + delay;
        }

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }
    }

    private void Store(string key, string value)
    {
        if (_cache.Count >= CacheLimit && _cacheOrder.Count > 0)
        {
            string oldest = _cacheOrder.Dequeue();
            _cache.Remove(oldest);
        }

        _cache[key] = value;
        _cacheOrder.Enqueue(key);
    }
}
