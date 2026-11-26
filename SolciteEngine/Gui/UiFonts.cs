using SolciteEngine.Utils;
using SkiaSharp;

namespace SolciteEngine.Gui;

public enum FontWeight
{
    Regular,
    Medium,
    SemiBold,
    Bold,
}

/// <summary>
/// The UI's real fonts, loaded from <c>Assets/Fonts</c> (all SIL Open Font License): the editor uses Inter
/// (<see cref="Family"/>), the same typeface as the Unity editor's interface; the game's UI uses Liberation Sans
/// (<see cref="GameFamily"/>), the font of Unity's TextMeshPro (LiberationSans SDF). JetBrainsMono is bundled too.
/// A weight without its own file uses the closest one (Medium → Regular, SemiBold → Bold); if Regular is missing the
/// system font is used. Put your own <c>&lt;Family&gt;-Regular.ttf</c> (or .otf) files there to use another family.
/// </summary>
public sealed class UiFonts : IDisposable
{
    /// <summary>The font family: files named &lt;Family&gt;-&lt;Weight&gt;.ttf or .otf in Assets/Fonts.</summary>
    public const string Family = "Inter";

    /// <summary>The font family of the game's UI (<c>UiCanvas</c>): Liberation Sans, TextMeshPro's default font.</summary>
    public const string GameFamily = "LiberationSans";

    private readonly Dictionary<FontWeight, SKTypeface> _faces = new();
    private readonly Dictionary<(int size, FontWeight weight), SKFont> _fonts = new();

    public UiFonts(string directory, string family = Family)
    {
        FamilyName = family;
        _faces[FontWeight.Regular] = Load(directory, $"{family}-Regular", null);
        _faces[FontWeight.Bold] = Load(directory, $"{family}-Bold", _faces[FontWeight.Regular]);
        _faces[FontWeight.Medium] = Load(directory, $"{family}-Medium", _faces[FontWeight.Regular]);
        _faces[FontWeight.SemiBold] = Load(directory, $"{family}-SemiBold", _faces[FontWeight.Bold]);
    }

    /// <summary>The family these fonts were loaded from ("Inter", "LiberationSans"...).</summary>
    public string FamilyName { get; }

    private static SKTypeface Load(string directory, string name, SKTypeface? fallback)
    {
        foreach (var extension in new[] { ".ttf", ".otf" })
        {
            string path = Path.Combine(directory, name + extension);
            if (!File.Exists(path)) continue;
            var face = SKTypeface.FromFile(path);
            if (face != null) return face;
        }
        if (fallback != null) return fallback; // a weight the family does not have: the closest one

        string file = name + ".ttf";
        Log.Warn($"UI font '{file}' not found in '{directory}'; using the system font.");
        return SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
    }

    /// <summary>A font of the given size (in UI points) and weight, cached.</summary>
    public SKFont Get(float size, FontWeight weight = FontWeight.Regular)
    {
        var key = ((int)MathF.Round(size * 4f), weight);
        if (!_fonts.TryGetValue(key, out var font))
        {
            font = new SKFont(_faces[weight], size)
            {
                Subpixel = true,
                Edging = SKFontEdging.Antialias,
                Hinting = SKFontHinting.None,
            };
            _fonts[key] = font;
        }
        return font;
    }

    public float Measure(string text, float size, FontWeight weight = FontWeight.Regular) =>
        text.Length == 0 ? 0f : Get(size, weight).MeasureText(text);

    /// <summary>Distance from the vertical centre of a line of text to its baseline, so text can be centred in a box.</summary>
    public float CenterToBaseline(float size, FontWeight weight = FontWeight.Regular)
    {
        var metrics = Get(size, weight).Metrics;
        return -(metrics.Ascent + metrics.Descent) * 0.5f;
    }

    public void Dispose()
    {
        foreach (var font in _fonts.Values) font.Dispose();
        _fonts.Clear();
        foreach (var face in _faces.Values.Distinct()) face.Dispose();
        _faces.Clear();
    }
}
