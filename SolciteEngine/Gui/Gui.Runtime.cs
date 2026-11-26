using SkiaSharp;

namespace SolciteEngine.Gui;

/// <summary>How a shape is filled: one colour, or a linear gradient from <see cref="From"/> to <see cref="To"/>.</summary>
public readonly record struct UiFill(SKColor From, SKColor To, float Angle, bool IsGradient)
{
    public static UiFill Solid(SKColor color) => new(color, color, 0f, false);

    public static UiFill Gradient(SKColor from, SKColor to, float angleDegrees) => new(from, to, angleDegrees, true);

    public bool IsVisible => From.Alpha > 0 || (IsGradient && To.Alpha > 0);

    internal UiFill Map(Func<SKColor, SKColor> f) => this with { From = f(From), To = f(To) };
}

// Widgets drawn in an exact rectangle instead of following the layout. The game's UI (UiCanvas) uses them, so the
// HUD, menus and the editor all share the same look and input handling.
public sealed partial class Gui
{
    private void Claim(UiRect rect)
    {
        _lastItemRect = rect;
        if (_window is { Regions.Count: > 0 }) CurrentRegion.MaxY = MathF.Max(CurrentRegion.MaxY, rect.Bottom);
    }

    public void PanelRect(UiRect rect, SKColor fill, float radius, SKColor border, float borderWidth = 0f) =>
        PanelRect(rect, UiFill.Solid(fill), radius, border, borderWidth);

    public void PanelRect(UiRect rect, UiFill fill, float radius, SKColor border, float borderWidth = 0f)
    {
        Claim(rect);
        if (fill.From.Alpha > 20 || (fill.IsGradient && fill.To.Alpha > 20)) BlockMouse(rect);
        FillRect(rect, radius, fill);
        if (borderWidth > 0f && border.Alpha > 0)
        {
            _paint.Style = SKPaintStyle.Stroke;
            _paint.StrokeWidth = borderWidth;
            _paint.Color = border;
            Canvas.DrawRoundRect(rect.Deflate(borderWidth * 0.5f).ToSk(), radius, radius, _paint);
        }
    }

    public void LabelRect(UiRect rect, string text, SKColor color, float size, FontWeight weight, TextAlign align)
    {
        Claim(rect);
        DrawText(Canvas, text, rect, color, align, size, weight);
    }

    public bool ButtonRect(UiRect rect, string id, string text, SKColor fill, SKColor textColor, float radius, float fontSize, bool enabled = true) =>
        ButtonRect(rect, id, text, UiFill.Solid(fill), textColor, radius, fontSize, enabled);

    public bool ButtonRect(UiRect rect, string id, string text, UiFill fill, SKColor textColor, float radius, float fontSize, bool enabled = true)
    {
        Claim(rect);
        int bid = GetId(id);
        bool hovered = false, held = false, clicked = false;
        if (enabled) clicked = ButtonBehavior(bid, rect, out hovered, out held);
        float hv = Fade(bid, hovered);
        var color = fill.Map(c => held ? Lerp(c, SKColors.Black, 0.2f) : Lerp(c, SKColors.White, 0.14f * hv));
        if (!enabled) color = fill.Map(c => WithAlpha(c, (byte)(c.Alpha / 2)));
        FillRect(rect, radius, color);
        DrawText(Canvas, text, rect.Deflate(6f, 0f), enabled ? textColor : WithAlpha(textColor, 120), TextAlign.Center, fontSize, FontWeight.Medium);
        return clicked;
    }

    public bool SliderRect(UiRect rect, string id, ref float value, float min, float max, string format = "0.##")
    {
        Claim(rect);
        return SliderCore(GetId(id), rect, ref value, min, max, format);
    }

    public bool CheckboxRect(UiRect rect, string id, string text, ref bool value, SKColor textColor, float fontSize)
    {
        Claim(rect);
        int cid = GetId(id);
        bool clicked = ButtonBehavior(cid, rect, out bool hovered, out _);
        if (clicked) value = !value;
        float t = Fade(cid, value);
        float hv = Fade(HashCode.Combine(cid, 1), hovered);

        float box = MathF.Min(rect.Height - 4f, 22f);
        var b = new UiRect(rect.X, rect.CenterY - box * 0.5f, box, box);
        FillRect(Canvas, b, 4f, Lerp(Lerp(Theme.Frame, Theme.FrameHover, hv), Theme.Accent, t));
        StrokeRect(Canvas, b.Deflate(0.5f), 4f, Lerp(Lerp(Theme.Border, Theme.BorderHover, hv), Theme.Accent, t));
        if (t > 0.05f) Icons.Draw(Canvas, IconKind.Check, b.Deflate(box * 0.17f), WithAlpha(SKColors.White, (byte)(255 * t)), 2f);
        DrawText(Canvas, text, new UiRect(b.Right + 8f, rect.Y, rect.Width - box - 8f, rect.Height), textColor, TextAlign.Left, fontSize);
        return clicked;
    }

    public bool TextBoxRect(UiRect rect, string id, ref string text, string? hint = null)
    {
        Claim(rect);
        bool changed = TextEditCore(GetId(id), rect, text, false, hint, out string result);
        if (changed) text = result;
        return changed;
    }

    public void ProgressRect(UiRect rect, float fraction, SKColor fill, SKColor back, float radius)
    {
        Claim(rect);
        FillRect(Canvas, rect, radius, back);
        float f = Math.Clamp(fraction, 0f, 1f);
        if (f <= 0f) return;
        Canvas.Save();
        Canvas.ClipRoundRect(new SKRoundRect(rect.ToSk(), radius, radius), SKClipOperation.Intersect, true);
        FillRect(Canvas, new UiRect(rect.X, rect.Y, MathF.Max(MathF.Min(rect.Height, rect.Width), rect.Width * f), rect.Height), radius, fill);
        Canvas.Restore();
    }

    public void ImageRect(UiRect rect, SKImage? image, SKColor tint, float radius)
    {
        Claim(rect);
        if (image == null)
        {
            FillRect(Canvas, rect, radius, new SKColor(60, 64, 74));
            Icons.Draw(Canvas, IconKind.Image, rect.Deflate(MathF.Min(rect.Width, rect.Height) * 0.3f), Theme.TextDim);
            return;
        }
        Canvas.Save();
        Canvas.ClipRoundRect(new SKRoundRect(rect.ToSk(), radius, radius), SKClipOperation.Intersect, true);
        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = tint;
        _paint.ColorFilter = tint == SKColors.White ? null : SKColorFilter.CreateBlendMode(tint, SKBlendMode.Modulate);
        Canvas.DrawImage(image, rect.ToSk(), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), _paint);
        _paint.ColorFilter?.Dispose();
        _paint.ColorFilter = null;
        Canvas.Restore();
    }

    /// <summary>A clickable picture: <paramref name="back"/> behind it, lighter when hovered, darker when pressed.</summary>
    public bool ImageButtonRect(UiRect rect, string id, SKImage? image, SKColor tint, UiFill back, float radius, bool enabled = true)
    {
        Claim(rect);
        int bid = GetId(id);
        bool hovered = false, held = false, clicked = false;
        if (enabled) clicked = ButtonBehavior(bid, rect, out hovered, out held);
        float hv = Fade(bid, hovered);
        if (back.IsVisible) FillRect(rect, radius, back);
        var t = held ? Lerp(tint, SKColors.Black, 0.25f) : tint;
        if (!enabled) t = WithAlpha(t, (byte)(t.Alpha / 2));
        ImageRect(rect, image, t, radius);
        if (hv > 0.01f && !held) FillRect(Canvas, rect, radius, new SKColor(255, 255, 255, (byte)(30 * hv)));
        return clicked;
    }

    /// <summary>A straight line from <paramref name="a"/> to <paramref name="b"/> (screen pixels) with round ends.</summary>
    public void LineRect(UiRect bounds, SKPoint a, SKPoint b, SKColor color, float thickness)
    {
        Claim(bounds);
        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = MathF.Max(0.5f, thickness);
        _paint.StrokeCap = SKStrokeCap.Round;
        _paint.Color = color;
        Canvas.DrawLine(a, b, _paint);
        _paint.StrokeCap = SKStrokeCap.Butt;
        _paint.Style = SKPaintStyle.Fill;
    }

    /// <summary>A rectangle, filled with <paramref name="fill"/> or (when <paramref name="filled"/> is off) outlined.</summary>
    public void BoxRect(UiRect rect, UiFill fill, float radius, bool filled, float thickness)
    {
        Claim(rect);
        if (filled) FillRect(rect, radius, fill);
        else StrokePath(fill, rect, thickness, (c, p) => c.DrawRoundRect(rect.Deflate(thickness * 0.5f).ToSk(), radius, radius, p));
    }

    /// <summary>An ellipse that fits <paramref name="rect"/>.</summary>
    public void EllipseRect(UiRect rect, UiFill fill, bool filled, float thickness)
    {
        Claim(rect);
        if (filled) Paint(fill, rect, SKPaintStyle.Fill, 0f, (c, p) => c.DrawOval(rect.ToSk(), p));
        else StrokePath(fill, rect, thickness, (c, p) => c.DrawOval(rect.Deflate(thickness * 0.5f).ToSk(), p));
    }

    /// <summary>A triangle pointing up that fits <paramref name="rect"/>.</summary>
    public void TriangleRect(UiRect rect, UiFill fill, bool filled, float thickness)
    {
        Claim(rect);
        var r = filled ? rect : rect.Deflate(thickness * 0.5f);
        using var path = new SKPath();
        path.MoveTo(r.CenterX, r.Y);
        path.LineTo(r.Right, r.Bottom);
        path.LineTo(r.X, r.Bottom);
        path.Close();
        if (filled) Paint(fill, rect, SKPaintStyle.Fill, 0f, (c, p) => c.DrawPath(path, p));
        else StrokePath(fill, rect, thickness, (c, p) => c.DrawPath(path, p));
    }

    // ---- fills (solid or gradient) ---------------------------------------------------------------

    private void FillRect(UiRect rect, float radius, UiFill fill)
    {
        if (!fill.IsGradient)
        {
            FillRect(Canvas, rect, radius, fill.From);
            return;
        }
        Paint(fill, rect, SKPaintStyle.Fill, 0f, (c, p) =>
        {
            if (radius > 0.5f) c.DrawRoundRect(rect.ToSk(), radius, radius, p);
            else c.DrawRect(rect.ToSk(), p);
        });
    }

    private void StrokePath(UiFill fill, UiRect rect, float thickness, Action<SKCanvas, SKPaint> draw) =>
        Paint(fill, rect, SKPaintStyle.Stroke, MathF.Max(0.5f, thickness), draw);

    private void Paint(UiFill fill, UiRect rect, SKPaintStyle style, float strokeWidth, Action<SKCanvas, SKPaint> draw)
    {
        _paint.Style = style;
        _paint.StrokeWidth = strokeWidth;
        _paint.Color = fill.IsGradient ? SKColors.White : fill.From;
        using var shader = fill.IsGradient ? GradientShader(rect, fill) : null;
        _paint.Shader = shader;
        draw(Canvas, _paint);
        _paint.Shader = null;
        _paint.Style = SKPaintStyle.Fill;
    }

    private static SKShader GradientShader(UiRect rect, UiFill fill)
    {
        float angle = fill.Angle * MathF.PI / 180f;
        float dx = MathF.Cos(angle), dy = MathF.Sin(angle);
        float half = MathF.Abs(rect.Width * 0.5f * dx) + MathF.Abs(rect.Height * 0.5f * dy);
        var start = new SKPoint(rect.CenterX - dx * half, rect.CenterY - dy * half);
        var end = new SKPoint(rect.CenterX + dx * half, rect.CenterY + dy * half);
        return SKShader.CreateLinearGradient(start, end, new[] { fill.From, fill.To }, SKShaderTileMode.Clamp);
    }

    /// <summary>Dashed outline and corner handles around a rectangle (selection in the UI designer).</summary>
    public void SelectionBox(UiRect rect, bool handles)
    {
        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = 1.5f;
        _paint.Color = Theme.AccentHover;
        Overlay.DrawRect(rect.ToSk(), _paint);
        if (!handles) return;
        _paint.Style = SKPaintStyle.Fill;
        foreach (var p in new[] { (rect.X, rect.Y), (rect.Right, rect.Y), (rect.X, rect.Bottom), (rect.Right, rect.Bottom) })
        {
            _paint.Color = SKColors.White;
            Overlay.DrawRect(p.Item1 - 4f, p.Item2 - 4f, p.Item1 + 4f, p.Item2 + 4f, _paint);
            _paint.Color = Theme.Accent;
            Overlay.DrawRect(p.Item1 - 3f, p.Item2 - 3f, p.Item1 + 3f, p.Item2 + 3f, _paint);
        }
    }
}
