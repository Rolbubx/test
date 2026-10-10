using OpenTK.Mathematics;
using SkiaSharp;

namespace GT67.Gui;

public enum ButtonStyle
{
    Normal,
    Primary,
    Danger,
}

public sealed partial class Gui
{
    private static SKColor Lerp(SKColor a, SKColor b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new SKColor(
            (byte)(a.Red + (b.Red - a.Red) * t), (byte)(a.Green + (b.Green - a.Green) * t),
            (byte)(a.Blue + (b.Blue - a.Blue) * t), (byte)(a.Alpha + (b.Alpha - a.Alpha) * t));
    }

    private static SKColor WithAlpha(SKColor c, byte alpha) => new(c.Red, c.Green, c.Blue, alpha);

    // ---- buttons ---------------------------------------------------------------------------------

    /// <summary>A push button. Returns true on the frame it is clicked. width 0 fits the text.</summary>
    public bool Button(string label, float width = 0f, ButtonStyle style = ButtonStyle.Normal)
    {
        string text = Visible(label);
        float w = width > 0f ? width : CurrentRegion.NextItemWidth > 0f ? CurrentRegion.NextItemWidth : Fonts.Measure(text, Theme.FontSize, FontWeight.Medium) + 28f;
        var rect = AllocItem(w, Theme.ButtonHeight);
        int id = GetId(label);
        bool clicked = ButtonBehavior(id, rect, out bool hovered, out bool held);
        float hv = Fade(id, hovered);

        SKColor normal, hover, pressed;
        switch (style)
        {
            case ButtonStyle.Primary: normal = Theme.Accent; hover = Theme.AccentHover; pressed = Theme.AccentPressed; break;
            case ButtonStyle.Danger: normal = Theme.Danger; hover = Lerp(Theme.Danger, SKColors.White, 0.18f); pressed = Lerp(Theme.Danger, SKColors.Black, 0.2f); break;
            default: normal = Theme.Button; hover = Theme.ButtonHover; pressed = Theme.ButtonPressed; break;
        }
        var fill = held ? pressed : Lerp(normal, hover, hv);
        FillRect(Canvas, rect, Theme.Rounding, fill);
        if (style == ButtonStyle.Normal) StrokeRect(Canvas, rect.Deflate(0.5f), Theme.Rounding, Lerp(Theme.Border, Theme.BorderHover, hv));
        DrawText(Canvas, text, rect.Deflate(8f, 0f), style == ButtonStyle.Normal ? Theme.Text : SKColors.White, TextAlign.Center, Theme.FontSize, FontWeight.Medium);
        return clicked;
    }

    /// <summary>A flat square button with an icon (toolbars). <paramref name="active"/> highlights it as selected.</summary>
    public bool IconButton(IconKind icon, string id, bool active = false, float size = 0f)
    {
        float s = size > 0f ? size : Theme.ButtonHeight;
        var rect = AllocItem(s, s);
        int bid = GetId(id);
        bool clicked = ButtonBehavior(bid, rect, out bool hovered, out bool held);
        float hv = Fade(bid, hovered);

        if (active) FillRect(Canvas, rect, Theme.Rounding, WithAlpha(Theme.Accent, 70));
        else if (hv > 0f || held) FillRect(Canvas, rect, Theme.Rounding, held ? Theme.ButtonPressed : WithAlpha(Theme.ButtonHover, (byte)(255 * hv)));
        var color = active ? Theme.AccentHover : Lerp(Theme.TextDim, Theme.Text, hv);
        Icons.Draw(Canvas, icon, rect.Deflate(s * 0.22f), color);
        return clicked;
    }

    /// <summary>A button with an icon on the left of its text.</summary>
    public bool IconTextButton(IconKind icon, string label, float width = 0f, ButtonStyle style = ButtonStyle.Normal)
    {
        string text = Visible(label);
        float w = width > 0f ? width : Fonts.Measure(text, Theme.FontSize, FontWeight.Medium) + 48f;
        var rect = AllocItem(w, Theme.ButtonHeight);
        int id = GetId(label);
        bool clicked = ButtonBehavior(id, rect, out bool hovered, out bool held);
        float hv = Fade(id, hovered);

        SKColor normal = style switch { ButtonStyle.Primary => Theme.Accent, ButtonStyle.Danger => Theme.Danger, _ => Theme.Button };
        SKColor hover = style switch { ButtonStyle.Primary => Theme.AccentHover, ButtonStyle.Danger => Lerp(Theme.Danger, SKColors.White, 0.18f), _ => Theme.ButtonHover };
        FillRect(Canvas, rect, Theme.Rounding, held ? Lerp(normal, SKColors.Black, 0.2f) : Lerp(normal, hover, hv));
        if (style == ButtonStyle.Normal) StrokeRect(Canvas, rect.Deflate(0.5f), Theme.Rounding, Lerp(Theme.Border, Theme.BorderHover, hv));

        var color = style == ButtonStyle.Normal ? Theme.Text : SKColors.White;
        Icons.Draw(Canvas, icon, new UiRect(rect.X + 9f, rect.Y + 6f, 16f, rect.Height - 12f), color);
        DrawText(Canvas, text, new UiRect(rect.X + 30f, rect.Y, rect.Width - 36f, rect.Height), color, TextAlign.Left, Theme.FontSize, FontWeight.Medium);
        return clicked;
    }

    // ---- toggles ---------------------------------------------------------------------------------

    /// <summary>A check box with its label on the right.</summary>
    public bool Checkbox(string label, ref bool value, float height = 22f)
    {
        string text = Visible(label);
        float w = text.Length == 0 ? 18f : Fonts.Measure(text, Theme.FontSize) + 28f;
        var rect = AllocItem(w, height);
        bool changed = DrawCheckbox(GetId(label), rect, text, ref value);
        return changed;
    }

    /// <summary>A property row: the label on the left, the check box on the right.</summary>
    public bool Toggle(string label, ref bool value)
    {
        var field = BeginField(label, Theme.FrameHeight);
        var box = new UiRect(field.X, field.Y + 2f, field.Width, 22f);
        return DrawCheckbox(GetId(label), box.WithWidth(22f), "", ref value);
    }

    private bool DrawCheckbox(int id, UiRect rect, string text, ref bool value)
    {
        bool clicked = ButtonBehavior(id, rect, out bool hovered, out _);
        if (clicked) value = !value;
        float t = Fade(id, value);
        float hv = Fade(HashCode.Combine(id, 1), hovered);

        var box = new UiRect(rect.X, rect.Y + (rect.Height - 16f) * 0.5f, 16f, 16f);
        FillRect(Canvas, box, 4f, Lerp(Lerp(Theme.Frame, Theme.FrameHover, hv), Theme.Accent, t));
        StrokeRect(Canvas, box.Deflate(0.5f), 4f, Lerp(Lerp(Theme.Border, Theme.BorderHover, hv), Theme.Accent, t));
        if (t > 0.05f) Icons.Draw(Canvas, IconKind.Check, box.Deflate(2.5f), WithAlpha(SKColors.White, (byte)(255 * t)), 1.9f);
        if (text.Length > 0)
            DrawText(Canvas, text, new UiRect(box.Right + 8f, rect.Y, rect.Width - 24f, rect.Height), Theme.Text, TextAlign.Left, Theme.FontSize);
        return clicked;
    }

    public bool RadioButton(string label, bool active)
    {
        string text = Visible(label);
        var rect = AllocItem(Fonts.Measure(text, Theme.FontSize) + 28f, 22f);
        int id = GetId(label);
        bool clicked = ButtonBehavior(id, rect, out bool hovered, out _);
        float t = Fade(id, active);
        float hv = Fade(HashCode.Combine(id, 1), hovered);

        var center = new SKPoint(rect.X + 8f, rect.CenterY);
        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = Lerp(Theme.Frame, Theme.FrameHover, hv);
        Canvas.DrawCircle(center, 8f, _paint);
        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = 1f;
        _paint.Color = Lerp(Lerp(Theme.Border, Theme.BorderHover, hv), Theme.Accent, t);
        Canvas.DrawCircle(center, 7.5f, _paint);
        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = WithAlpha(Theme.Accent, (byte)(255 * t));
        Canvas.DrawCircle(center, 4f, _paint);
        DrawText(Canvas, text, new UiRect(rect.X + 22f, rect.Y, rect.Width - 22f, rect.Height), Theme.Text, TextAlign.Left, Theme.FontSize);
        return clicked;
    }

    // ---- sliders and drag fields -----------------------------------------------------------------

    /// <summary>A slider with the label on the left and the value inside the bar (double-click to type a value).</summary>
    public bool SliderFloat(string label, ref float value, float min, float max, string format = "0.00")
    {
        var field = BeginField(label, Theme.FrameHeight);
        int id = GetId(label);
        var rect = field.Deflate(0f, 2f);
        return SliderCore(id, rect, ref value, min, max, format);
    }

    public bool SliderInt(string label, ref int value, int min, int max)
    {
        float v = value;
        var field = BeginField(label, Theme.FrameHeight);
        bool changed = SliderCore(GetId(label), field.Deflate(0f, 2f), ref v, min, max, "0");
        if (changed) value = (int)MathF.Round(v);
        return changed;
    }

    private bool SliderCore(int id, UiRect rect, ref float value, float min, float max, string format)
    {
        if (_focusId == id) return NumberEdit(id, rect, ref value, min, max, format);

        bool hovered = IsHovered(rect) && (_activeId == 0 || _activeId == id);
        if (hovered && Input.MouseDoubleClicked[0])
        {
            BeginNumberEdit(id, value, format);
            _activeId = 0;
            return false;
        }
        if (hovered && Input.MousePressed[0]) _activeId = id;

        bool changed = false;
        if (_activeId == id)
        {
            if (Input.MouseDown[0])
            {
                float t = Math.Clamp((Input.MousePosition.X - rect.X - 5f) / MathF.Max(1f, rect.Width - 10f), 0f, 1f);
                float next = min + t * (max - min);
                if (MathF.Abs(next - value) > 1e-6f)
                {
                    value = next;
                    changed = true;
                }
            }
            else _activeId = 0;
        }

        float hv = Fade(id, hovered || _activeId == id);
        FillRect(Canvas, rect, 5f, Lerp(Theme.Frame, Theme.FrameHover, hv));
        float fraction = max > min ? Math.Clamp((value - min) / (max - min), 0f, 1f) : 0f;
        if (fraction > 0f)
        {
            var fill = new UiRect(rect.X, rect.Y, MathF.Max(10f, rect.Width * fraction), rect.Height);
            Canvas.Save();
            Canvas.ClipRoundRect(new SKRoundRect(rect.ToSk(), 5f, 5f), SKClipOperation.Intersect, true);
            FillRect(Canvas, fill, 5f, WithAlpha(Lerp(Theme.Accent, Theme.AccentHover, hv), 200));
            Canvas.Restore();
        }
        StrokeRect(Canvas, rect.Deflate(0.5f), 5f, Lerp(Theme.Border, Theme.BorderHover, hv));
        DrawText(Canvas, value.ToString(format, System.Globalization.CultureInfo.InvariantCulture), rect, Theme.Text, TextAlign.Center, Theme.FontSize, FontWeight.Medium);
        return changed;
    }

    /// <summary>A number field: drag left/right to change it, click to type.</summary>
    public bool DragFloat(string label, ref float value, float speed = 0.05f, float min = float.MinValue, float max = float.MaxValue, string format = "0.###")
    {
        var field = BeginField(label, Theme.FrameHeight);
        return DragCore(GetId(label), field.Deflate(0f, 2f), ref value, speed, min, max, format, null, default);
    }

    public bool DragInt(string label, ref int value, int min = int.MinValue, int max = int.MaxValue)
    {
        float v = value;
        var field = BeginField(label, Theme.FrameHeight);
        bool changed = DragCore(GetId(label), field.Deflate(0f, 2f), ref v, 0.15f, min, max, "0", null, default);
        if (changed) value = (int)MathF.Round(v);
        return changed;
    }

    /// <summary>X / Y / Z fields in one row, with coloured axis letters.</summary>
    public bool DragVector3(string label, ref Vector3 value, float speed = 0.05f, string format = "0.###")
    {
        var field = BeginField(label, Theme.FrameHeight).Deflate(0f, 2f);
        int id = GetId(label);
        float cell = (field.Width - 8f) / 3f;
        bool changed = false;
        float x = value.X, y = value.Y, z = value.Z;
        changed |= DragCore(HashCode.Combine(id, 0), new UiRect(field.X, field.Y, cell, field.Height), ref x, speed, float.MinValue, float.MaxValue, format, "X", Theme.AxisX);
        changed |= DragCore(HashCode.Combine(id, 1), new UiRect(field.X + cell + 4f, field.Y, cell, field.Height), ref y, speed, float.MinValue, float.MaxValue, format, "Y", Theme.AxisY);
        changed |= DragCore(HashCode.Combine(id, 2), new UiRect(field.X + 2f * (cell + 4f), field.Y, cell, field.Height), ref z, speed, float.MinValue, float.MaxValue, format, "Z", Theme.AxisZ);
        if (changed) value = new Vector3(x, y, z);
        return changed;
    }

    public bool DragVector2(string label, ref Vector2 value, float speed = 0.05f, string format = "0.###")
    {
        var field = BeginField(label, Theme.FrameHeight).Deflate(0f, 2f);
        int id = GetId(label);
        float cell = (field.Width - 4f) / 2f;
        float x = value.X, y = value.Y;
        bool changed = DragCore(HashCode.Combine(id, 0), new UiRect(field.X, field.Y, cell, field.Height), ref x, speed, float.MinValue, float.MaxValue, format, "X", Theme.AxisX);
        changed |= DragCore(HashCode.Combine(id, 1), new UiRect(field.X + cell + 4f, field.Y, cell, field.Height), ref y, speed, float.MinValue, float.MaxValue, format, "Y", Theme.AxisY);
        if (changed) value = new Vector2(x, y);
        return changed;
    }

    public bool DragVector4(string label, ref Vector4 value, float speed = 0.05f, string format = "0.###")
    {
        var field = BeginField(label, Theme.FrameHeight).Deflate(0f, 2f);
        int id = GetId(label);
        float cell = (field.Width - 12f) / 4f;
        float[] v = { value.X, value.Y, value.Z, value.W };
        string[] names = { "X", "Y", "Z", "W" };
        SKColor[] colors = { Theme.AxisX, Theme.AxisY, Theme.AxisZ, Theme.TextDim };
        bool changed = false;
        for (int i = 0; i < 4; i++)
            changed |= DragCore(HashCode.Combine(id, i), new UiRect(field.X + i * (cell + 4f), field.Y, cell, field.Height), ref v[i], speed, float.MinValue, float.MaxValue, format, names[i], colors[i]);
        if (changed) value = new Vector4(v[0], v[1], v[2], v[3]);
        return changed;
    }

    private bool DragCore(int id, UiRect rect, ref float value, float speed, float min, float max, string format, string? letter, SKColor letterColor)
    {
        if (_focusId == id) return NumberEdit(id, rect, ref value, min, max, format);

        bool hovered = IsHovered(rect) && (_activeId == 0 || _activeId == id);
        if (hovered && Input.MousePressed[0])
        {
            _activeId = id;
            _dragStartX = Input.MousePosition.X;
            _dragStartValue = value;
            _dragMoved = false;
        }

        bool changed = false;
        if (_activeId == id)
        {
            if (Input.MouseDown[0])
            {
                float dx = Input.MousePosition.X - _dragStartX;
                if (MathF.Abs(dx) > 3f) _dragMoved = true;
                if (_dragMoved)
                {
                    float scale = Input.Shift ? 0.1f : Input.Ctrl ? 10f : 1f;
                    float next = Math.Clamp(_dragStartValue + dx * speed * scale, min, max);
                    if (MathF.Abs(next - value) > 1e-7f)
                    {
                        value = next;
                        changed = true;
                    }
                }
            }
            else
            {
                if (!_dragMoved) BeginNumberEdit(id, value, format);
                _activeId = 0;
            }
        }

        float hv = Fade(id, hovered || _activeId == id);
        FillRect(Canvas, rect, 5f, Lerp(Theme.Frame, Theme.FrameHover, hv));
        StrokeRect(Canvas, rect.Deflate(0.5f), 5f, _activeId == id && _dragMoved ? Theme.Accent : Lerp(Theme.Border, Theme.BorderHover, hv));
        if (letter != null)
        {
            DrawText(Canvas, letter, new UiRect(rect.X + 7f, rect.Y, 14f, rect.Height), letterColor, TextAlign.Left, Theme.SmallFontSize, FontWeight.SemiBold);
            DrawText(Canvas, value.ToString(format, System.Globalization.CultureInfo.InvariantCulture), new UiRect(rect.X + 18f, rect.Y, rect.Width - 24f, rect.Height), Theme.Text, TextAlign.Right, Theme.FontSize);
        }
        else
        {
            DrawText(Canvas, value.ToString(format, System.Globalization.CultureInfo.InvariantCulture), rect, Theme.Text, TextAlign.Center, Theme.FontSize);
        }
        return changed;
    }

    private float _dragStartX, _dragStartValue;
    private bool _dragMoved;

    // ---- progress and rows -----------------------------------------------------------------------

    public void ProgressBar(float fraction, string? overlay = null, float width = 0f, float height = 16f)
    {
        var rect = AllocItem(width, height);
        FillRect(Canvas, rect, height * 0.5f, Theme.Frame);
        float f = Math.Clamp(fraction, 0f, 1f);
        if (f > 0f)
        {
            Canvas.Save();
            Canvas.ClipRoundRect(new SKRoundRect(rect.ToSk(), height * 0.5f, height * 0.5f), SKClipOperation.Intersect, true);
            FillRect(Canvas, new UiRect(rect.X, rect.Y, MathF.Max(height, rect.Width * f), height), height * 0.5f, Theme.Accent);
            Canvas.Restore();
        }
        if (overlay != null) DrawText(Canvas, overlay, rect, Theme.Text, TextAlign.Center, Theme.SmallFontSize, FontWeight.Medium);
    }

    /// <summary>A full-width clickable row (lists). Returns true when clicked; <see cref="LastItemDoubleClicked"/> tells about a double-click.</summary>
    public bool Selectable(string label, bool selected, IconKind icon = IconKind.None, float height = 0f)
    {
        float h = height > 0f ? height : Theme.RowHeight;
        var rect = AllocItem(0f, h);
        int id = GetId(label);
        bool clicked = ButtonBehavior(id, rect, out bool hovered, out bool held);
        LastItemDoubleClicked = hovered && Input.MouseDoubleClicked[0];
        float hv = Fade(id, hovered);

        if (selected) FillRect(Canvas, rect, 4f, Theme.Selection);
        else if (hv > 0f) FillRect(Canvas, rect, 4f, WithAlpha(Theme.Hover, (byte)(Theme.Hover.Alpha * hv)));

        float x = rect.X + 8f;
        if (icon != IconKind.None)
        {
            Icons.Draw(Canvas, icon, new UiRect(x, rect.Y + 4f, h - 8f, h - 8f), selected ? Theme.AccentHover : Theme.TextDim);
            x += h;
        }
        DrawText(Canvas, Visible(label), new UiRect(x, rect.Y, rect.Right - x - 6f, h), selected ? SKColors.White : Theme.Text, TextAlign.Left, Theme.FontSize);
        return clicked;
    }

    /// <summary>True if the last <see cref="Selectable"/> or tree row was double-clicked.</summary>
    public bool LastItemDoubleClicked { get; private set; }

    /// <summary>True if the mouse is over the most recently drawn item.</summary>
    public bool IsLastItemHovered => IsHovered(_lastItemRect);

    /// <summary>The screen rectangle of the most recently drawn item.</summary>
    public UiRect LastItemRect => _lastItemRect;
}
