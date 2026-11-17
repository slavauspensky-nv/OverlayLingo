using System.IO;
using System.Windows.Media;

namespace OverlayLingo;

/// <summary>Встроенные шрифты окна перевода (все с поддержкой кириллицы).</summary>
internal static class FontHelper
{
    public const string PixelName = "Press Start 2P";

    private static readonly (string Display, string File, string Family)[] Fonts =
    {
        ("Press Start 2P", "pressstart2p.ttf", "Press Start 2P"),
        ("Roboto", "roboto.ttf", "Roboto"),
        ("Open Sans", "opensans.ttf", "Open Sans"),
        ("Montserrat", "montserrat.ttf", "Montserrat"),
        ("Lato", "lato.ttf", "Lato"),
        ("PT Sans", "ptsans.ttf", "PT Sans"),
        ("PT Serif", "ptserif.ttf", "PT Serif"),
        ("Fira Sans", "firasans.ttf", "Fira Sans"),
        ("Fira Code", "firacode.ttf", "Fira Code"),
        ("JetBrains Mono", "jetbrainsmono.ttf", "JetBrains Mono"),
        ("Source Sans 3", "sourcesans3.ttf", "Source Sans 3"),
        ("Noto Sans", "notosans.ttf", "Noto Sans"),
        ("Ubuntu", "ubuntu.ttf", "Ubuntu"),
        ("Rubik", "rubik.ttf", "Rubik"),
        ("Nunito", "nunito.ttf", "Nunito"),
        ("Comfortaa", "comfortaa.ttf", "Comfortaa"),
        ("Philosopher", "philosopher.ttf", "Philosopher"),
        ("Alegreya", "alegreya.ttf", "Alegreya"),
        ("Lora", "lora.ttf", "Lora"),
        ("Playfair Display", "playfair.ttf", "Playfair Display"),
        ("Yeseva One", "yesevaone.ttf", "Yeseva One"),
        ("Oswald", "oswald.ttf", "Oswald"),
        ("Russo One", "russoone.ttf", "Russo One"),
        ("Caveat", "caveat.ttf", "Caveat"),
        ("Marck Script", "marckscript.ttf", "Marck Script")
    };

    public static IReadOnlyList<string> Names { get; } = Fonts.Select(font => font.Display).ToList();

    public static FontFamily Resolve(string name)
    {
        foreach (var font in Fonts)
        {
            if (!string.Equals(font.Display, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string path = Path.Combine(AppContext.BaseDirectory, "fonts", font.File);

            if (File.Exists(path))
            {
                try
                {
                    return new FontFamily(new Uri(path), "./#" + font.Family);
                }
                catch
                {
                }
            }

            break;
        }

        try
        {
            return new FontFamily(string.IsNullOrWhiteSpace(name) ? "Segoe UI" : name);
        }
        catch
        {
            return new FontFamily("Segoe UI");
        }
    }
}
