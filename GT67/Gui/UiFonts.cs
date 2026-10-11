using GT67.Utils;
using SkiaSharp;

namespace GT67.Gui;

public enum FontWeight
{
    Regular,
    Medium,
    SemiBold,
    Bold,
}

/// <summary>
/// The UI's real fonts: JetBrains Mono (Regular, Medium, SemiBold, Bold) loaded from <c>Assets/Fonts</c>. If a file is
/// missing the system font is used. Change <see cref="Family"/> to "Inter" for the proportional font that is also
/// bundled, or put your own <c>&lt;Family&gt;-Regular.ttf</c> (or .otf) files there.
/// </summary>
public sealed class UiFonts : IDisposable
{
    /// <summary>The font family: files named &lt;Family&gt;-&lt;Weight&gt;.ttf or .otf in Assets/Fonts.</summary>
    public const string Family = "JetBrainsMono";

    private readonly Dictionary<FontWeight, SKTypeface> _faces = new();
    private readonly Dictionary<(int size, FontWeight weight), SKFont> _fonts = new();

    public UiFonts(string directory)
    {
        _faces[FontWeight.Regular] = Load(directory, $"{Family}-Regular", null);
        _faces[FontWeight.Medium] = Load(directory, $"{Family}-Medium", _faces[FontWeight.Regular]);
        _faces[FontWeight.SemiBold] = Load(directory, $"{Family}-SemiBold", _faces[FontWeight.Regular]);
        _faces[FontWeight.Bold] = Load(directory, $"{Family}-Bold", _faces[FontWeight.SemiBold]);
    }

    private static SKTypeface Load(string directory, string name, SKTypeface? fallback)
    {
        foreach (var extension in new[] { ".ttf", ".otf" })
        {
            string path = Path.Combine(directory, name + extension);
            if (!File.Exists(path)) continue;
            var face = SKTypeface.FromFile(path);
            if (face != null) return face;
        }
        string file = name + ".ttf";

        Log.Warn($"UI font '{file}' not found in '{directory}'; using the system font.");
        return fallback ?? SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
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
