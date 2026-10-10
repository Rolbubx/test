using OpenTK.Mathematics;
using SkiaSharp;

namespace GT67.Gui;

public sealed partial class Gui
{
    private const float LineHeight = 18f;

    /// <summary>Draws one line of text centred vertically in <paramref name="rect"/>, shortened with "…" if it does not fit.</summary>
    private void DrawText(SKCanvas canvas, string text, UiRect rect, SKColor color, TextAlign align, float size,
        FontWeight weight = FontWeight.Regular, bool elide = true)
    {
        if (text.Length == 0 || rect.Width < 4f) return;

        var font = Fonts.Get(size, weight);
        float width = font.MeasureText(text);
        if (elide && width > rect.Width) text = Elide(font, text, rect.Width, out width);

        float x = align switch
        {
            TextAlign.Center => rect.CenterX - width * 0.5f,
            TextAlign.Right => rect.Right - width,
            _ => rect.X,
        };
        float baseline = rect.CenterY + Fonts.CenterToBaseline(size, weight);

        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = color;
        canvas.DrawText(text, x, baseline, SKTextAlign.Left, font, _paint);
    }

    private static string Elide(SKFont font, string text, float maxWidth, out float width)
    {
        const string ellipsis = "…";
        float ellipsisWidth = font.MeasureText(ellipsis);
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (font.MeasureText(text.AsSpan(0, mid)) + ellipsisWidth <= maxWidth) lo = mid;
            else hi = mid - 1;
        }
        string result = lo == 0 ? ellipsis : text[..lo].TrimEnd() + ellipsis;
        width = font.MeasureText(result);
        return result;
    }

    /// <summary>Width of a string in the default UI font, in UI points.</summary>
    public float MeasureText(string text, float size = 0f, FontWeight weight = FontWeight.Regular) =>
        Fonts.Measure(text, size > 0f ? size : Theme.FontSize, weight);

    // ---- text widgets ----------------------------------------------------------------------------

    public void Text(string text) => TextColored(Theme.Text, text);

    public void TextDim(string text) => TextColored(Theme.TextDim, text);

    public void TextColored(SKColor color, string text)
    {
        var rect = AllocItem(0f, LineHeight);
        DrawText(Canvas, text, rect, color, TextAlign.Left, Theme.FontSize);
    }

    /// <summary>A section title in semi-bold.</summary>
    public void Heading(string text, float width = 0f)
    {
        var rect = AllocItem(width, 24f);
        DrawText(Canvas, text, rect, Theme.Text, TextAlign.Left, Theme.HeadingFontSize, FontWeight.SemiBold);
    }

    /// <summary>Text that wraps at the width of the window.</summary>
    public void TextWrapped(string text, bool dim = false)
    {
        var font = Fonts.Get(Theme.FontSize);
        float width = AvailableWidth;
        var lines = Wrap(font, text, width);
        var color = dim ? Theme.TextDim : Theme.Text;
        foreach (var line in lines)
        {
            var rect = AllocItem(width, LineHeight);
            DrawText(Canvas, line, rect, color, TextAlign.Left, Theme.FontSize, elide: false);
        }
    }

    private static List<string> Wrap(SKFont font, string text, float width)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var words = paragraph.Split(' ');
            string current = "";
            foreach (var word in words)
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && font.MeasureText(candidate) > width)
                {
                    lines.Add(current);
                    current = word;
                }
                else current = candidate;
            }
            lines.Add(current);
        }
        return lines;
    }

    /// <summary>A dim name on the left and a value on the right.</summary>
    public void LabelValue(string label, string value)
    {
        var field = BeginField(label, 20f);
        DrawText(Canvas, value, field, Theme.Text, TextAlign.Left, Theme.FontSize);
    }

    /// <summary>A coloured status line (errors in red, warnings in amber).</summary>
    public void TextStatus(string text, bool error)
    {
        var font = Fonts.Get(Theme.FontSize);
        float width = AvailableWidth;
        foreach (var line in Wrap(font, text, width))
        {
            var rect = AllocItem(width, LineHeight);
            DrawText(Canvas, line, rect, error ? Theme.Danger : Theme.Text, TextAlign.Left, Theme.FontSize, elide: false);
        }
    }

    // ---- separators ------------------------------------------------------------------------------

    public void Separator()
    {
        var rect = AllocItem(0f, 9f);
        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = 1f;
        _paint.Color = Theme.Separator;
        Canvas.DrawLine(rect.X, rect.CenterY, rect.Right, rect.CenterY, _paint);
    }

    /// <summary>A small upper-case section title followed by a line.</summary>
    public void SeparatorText(string label)
    {
        var rect = AllocItem(0f, 22f);
        string text = Visible(label).ToUpperInvariant();
        var font = Fonts.Get(Theme.SmallFontSize, FontWeight.SemiBold);
        float w = font.MeasureText(text) + 2f;
        DrawText(Canvas, text, new UiRect(rect.X, rect.Y + 3f, w + 4f, rect.Height - 3f), Theme.TextDim, TextAlign.Left, Theme.SmallFontSize, FontWeight.SemiBold, elide: false);
        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = 1f;
        _paint.Color = Theme.Separator;
        float y = rect.CenterY + 2f;
        Canvas.DrawLine(rect.X + w + 8f, y, rect.Right, y, _paint);
    }

    /// <summary>Space on the line with nothing in it.</summary>
    public void Dummy(float width, float height) => AllocItem(width, height);
}
