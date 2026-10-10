using SkiaSharp;

namespace GT67.Gui;

// Widgets drawn in an exact rectangle instead of following the layout. The game's UI (UiCanvas) uses them, so the
// HUD, menus and the editor all share the same look and input handling.
public sealed partial class Gui
{
    private void Claim(UiRect rect)
    {
        _lastItemRect = rect;
        if (_window is { Regions.Count: > 0 }) CurrentRegion.MaxY = MathF.Max(CurrentRegion.MaxY, rect.Bottom);
    }

    public void PanelRect(UiRect rect, SKColor fill, float radius, SKColor border, float borderWidth = 0f)
    {
        Claim(rect);
        if (fill.Alpha > 20) BlockMouse(rect);
        FillRect(Canvas, rect, radius, fill);
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

    public bool ButtonRect(UiRect rect, string id, string text, SKColor fill, SKColor textColor, float radius, float fontSize, bool enabled = true)
    {
        Claim(rect);
        int bid = GetId(id);
        bool hovered = false, held = false, clicked = false;
        if (enabled) clicked = ButtonBehavior(bid, rect, out hovered, out held);
        float hv = Fade(bid, hovered);
        var color = held ? Lerp(fill, SKColors.Black, 0.2f) : Lerp(fill, SKColors.White, 0.14f * hv);
        if (!enabled) color = WithAlpha(fill, (byte)(fill.Alpha / 2));
        FillRect(Canvas, rect, radius, color);
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
