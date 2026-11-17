using System.Windows.Media;

namespace OverlayLingo;

/// <summary>Разбор цвета из настроек и общие вычисления оформления.</summary>
internal static class StyleHelper
{
    public static Color ParseColor(string hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
            {
                var parsed = (Color)ColorConverter.ConvertFromString(hex);
                return Color.FromRgb(parsed.R, parsed.G, parsed.B);
            }
        }
        catch
        {
        }

        return fallback;
    }

    public static bool IsLight(Color color)
    {
        double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
        return luminance > 0.6;
    }
}
