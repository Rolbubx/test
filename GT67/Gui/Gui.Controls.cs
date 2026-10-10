using OpenTK.Mathematics;
using SkiaSharp;

namespace GT67.Gui;

public sealed partial class Gui
{
    private readonly Dictionary<int, bool> _open = new();
    private readonly Dictionary<int, float> _hues = new();

    // ---- combo boxes -----------------------------------------------------------------------------

    /// <summary>A drop-down list. Returns true when another entry was chosen.</summary>
    public bool Combo(string label, ref int index, IReadOnlyList<string> items)
    {
        var field = BeginField(label, Theme.FrameHeight).Deflate(0f, 2f);
        int id = GetId(label);
        bool clicked = ButtonBehavior(id, field, out bool hovered, out _);
        bool open = IsPopupOpen("##combo" + label);
        float hv = Fade(id, hovered || open);

        FillRect(Canvas, field, 5f, Lerp(Theme.Frame, Theme.FrameHover, hv));
        StrokeRect(Canvas, field.Deflate(0.5f), 5f, open ? Theme.Accent : Lerp(Theme.Border, Theme.BorderHover, hv));
        string current = index >= 0 && index < items.Count ? items[index] : "";
        bool none = current.Length == 0;
        DrawText(Canvas, none ? "Choose..." : current, new UiRect(field.X + 8f, field.Y, field.Width - 28f, field.Height), none ? Theme.TextDisabled : Theme.Text, TextAlign.Left, Theme.FontSize);
        Icons.Draw(Canvas, open ? IconKind.ChevronUp : IconKind.ChevronDown, new UiRect(field.Right - 20f, field.Y + 8f, 12f, field.Height - 16f), Theme.TextDim);
        if (clicked) TogglePopup("##combo" + label, field);

        bool changed = false;
        if (BeginPopup("##combo" + label, field.Width, 260f))
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (Selectable(items[i] + "##" + i, i == index))
                {
                    index = i;
                    changed = true;
                    CloseCurrentPopup();
                }
            }
            EndPopup();
        }
        return changed;
    }

    public bool EnumCombo<T>(string label, ref T value) where T : struct, Enum
    {
        var names = Enum.GetNames<T>();
        int index = Array.IndexOf(names, value.ToString());
        if (!Combo(label, ref index, names)) return false;
        value = Enum.Parse<T>(names[index]);
        return true;
    }

    // ---- colours ---------------------------------------------------------------------------------

    public bool ColorEdit(string label, ref Vector3 color)
    {
        var c = new Vector4(color, 1f);
        if (!ColorEditCore(label, ref c, false)) return false;
        color = c.Xyz;
        return true;
    }

    public bool ColorEdit(string label, ref Vector4 color) => ColorEditCore(label, ref color, true);

    private bool ColorEditCore(string label, ref Vector4 color, bool alpha)
    {
        var field = BeginField(label, Theme.FrameHeight).Deflate(0f, 2f);
        int id = GetId(label);
        string popup = "##color" + label;
        bool clicked = ButtonBehavior(id, field, out bool hovered, out _);
        float hv = Fade(id, hovered || IsPopupOpen(popup));

        FillRect(Canvas, field, 5f, Lerp(Theme.Frame, Theme.FrameHover, hv));
        var swatch = field.Deflate(3f);
        FillRect(Canvas, swatch, 3f, new SKColor(ToByte(color.X), ToByte(color.Y), ToByte(color.Z)));
        if (alpha && color.W < 1f) FillRect(Canvas, new UiRect(swatch.X, swatch.Y, swatch.Width * (1f - color.W), swatch.Height), 3f, new SKColor(40, 40, 46));
        StrokeRect(Canvas, field.Deflate(0.5f), 5f, Lerp(Theme.Border, Theme.BorderHover, hv));
        if (clicked) TogglePopup(popup, field);

        bool changed = false;
        if (BeginPopup(popup, 232f, 420f))
        {
            changed = ColorPicker(ref color, alpha, id);
            EndPopup();
        }
        return changed;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    private bool ColorPicker(ref Vector4 color, bool alpha, int id)
    {
        Rgb2Hsv(color.Xyz, out float h, out float s, out float v);
        if (_hues.TryGetValue(id, out float stored) && (s < 0.001f || v < 0.001f)) h = stored;
        else if (_hues.TryGetValue(id, out stored) && MathF.Abs(stored - h) < 0.01f) h = stored;
        bool changed = false;

        var sv = AllocItem(0f, 150f);
        if (SurfaceDrag(HashCode.Combine(id, "sv"), sv, out Vector2 p))
        {
            s = p.X;
            v = 1f - p.Y;
            changed = true;
        }
        Canvas.Save();
        Canvas.ClipRoundRect(new SKRoundRect(sv.ToSk(), 5f, 5f), SKClipOperation.Intersect, true);
        var hueColor = Hsv2Rgb(h, 1f, 1f);
        _paint.Style = SKPaintStyle.Fill;
        _paint.Shader = SKShader.CreateLinearGradient(new SKPoint(sv.X, 0), new SKPoint(sv.Right, 0),
            new[] { SKColors.White, new SKColor(ToByte(hueColor.X), ToByte(hueColor.Y), ToByte(hueColor.Z)) }, null, SKShaderTileMode.Clamp);
        Canvas.DrawRect(sv.ToSk(), _paint);
        _paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, sv.Y), new SKPoint(0, sv.Bottom),
            new[] { SKColors.Transparent, SKColors.Black }, null, SKShaderTileMode.Clamp);
        Canvas.DrawRect(sv.ToSk(), _paint);
        _paint.Shader = null;
        Canvas.Restore();
        DrawHandle(new SKPoint(sv.X + s * sv.Width, sv.Y + (1f - v) * sv.Height));

        var hb = AllocItem(0f, 14f);
        if (SurfaceDrag(HashCode.Combine(id, "hue"), hb, out p))
        {
            h = p.X;
            changed = true;
        }
        var stops = new SKColor[7];
        for (int i = 0; i < 7; i++)
        {
            var c = Hsv2Rgb(i / 6f, 1f, 1f);
            stops[i] = new SKColor(ToByte(c.X), ToByte(c.Y), ToByte(c.Z));
        }
        _paint.Shader = SKShader.CreateLinearGradient(new SKPoint(hb.X, 0), new SKPoint(hb.Right, 0), stops, null, SKShaderTileMode.Clamp);
        Canvas.DrawRoundRect(hb.ToSk(), 4f, 4f, _paint);
        _paint.Shader = null;
        DrawHandle(new SKPoint(hb.X + h * hb.Width, hb.CenterY));

        float a = color.W;
        if (alpha)
        {
            var ab = AllocItem(0f, 14f);
            if (SurfaceDrag(HashCode.Combine(id, "alpha"), ab, out p))
            {
                a = p.X;
                changed = true;
            }
            FillRect(Canvas, ab, 4f, new SKColor(60, 64, 74));
            _paint.Shader = SKShader.CreateLinearGradient(new SKPoint(ab.X, 0), new SKPoint(ab.Right, 0),
                new[] { WithAlpha(new SKColor(ToByte(color.X), ToByte(color.Y), ToByte(color.Z)), 0), new SKColor(ToByte(color.X), ToByte(color.Y), ToByte(color.Z)) },
                null, SKShaderTileMode.Clamp);
            Canvas.DrawRoundRect(ab.ToSk(), 4f, 4f, _paint);
            _paint.Shader = null;
            DrawHandle(new SKPoint(ab.X + a * ab.Width, ab.CenterY));
        }

        if (changed)
        {
            _hues[id] = h;
            var rgb = Hsv2Rgb(h, s, v);
            color = new Vector4(rgb, a);
        }

        Spacing(2f);
        float r = color.X, g = color.Y, b = color.Z;
        bool edited = false;
        edited |= SliderFloat("R", ref r, 0f, 1f);
        edited |= SliderFloat("G", ref g, 0f, 1f);
        edited |= SliderFloat("B", ref b, 0f, 1f);
        if (edited)
        {
            color = new Vector4(r, g, b, color.W);
            changed = true;
        }
        return changed;
    }

    private void DrawHandle(SKPoint p)
    {
        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = 2f;
        _paint.Color = SKColors.White;
        Canvas.DrawCircle(p, 5.5f, _paint);
        _paint.StrokeWidth = 1f;
        _paint.Color = new SKColor(0, 0, 0, 160);
        Canvas.DrawCircle(p, 7f, _paint);
    }

    /// <summary>Drag inside a rectangle; gives the mouse position normalised to 0..1 while it is held.</summary>
    private bool SurfaceDrag(int id, UiRect rect, out Vector2 normalized)
    {
        normalized = default;
        bool hovered = IsHovered(rect) && (_activeId == 0 || _activeId == id);
        if (hovered && Input.MousePressed[0]) _activeId = id;
        if (_activeId != id) return false;
        if (!Input.MouseDown[0])
        {
            _activeId = 0;
            return false;
        }
        normalized = new Vector2(
            Math.Clamp((Input.MousePosition.X - rect.X) / MathF.Max(1f, rect.Width), 0f, 1f),
            Math.Clamp((Input.MousePosition.Y - rect.Y) / MathF.Max(1f, rect.Height), 0f, 1f));
        return true;
    }

    private static void Rgb2Hsv(Vector3 c, out float h, out float s, out float v)
    {
        float max = MathF.Max(c.X, MathF.Max(c.Y, c.Z)), min = MathF.Min(c.X, MathF.Min(c.Y, c.Z));
        float d = max - min;
        v = max;
        s = max <= 0f ? 0f : d / max;
        if (d <= 1e-6f) h = 0f;
        else if (max == c.X) h = ((c.Y - c.Z) / d + 6f) % 6f / 6f;
        else if (max == c.Y) h = ((c.Z - c.X) / d + 2f) / 6f;
        else h = ((c.X - c.Y) / d + 4f) / 6f;
    }

    private static Vector3 Hsv2Rgb(float h, float s, float v)
    {
        h = (h % 1f + 1f) % 1f * 6f;
        int i = (int)h;
        float f = h - i, p = v * (1f - s), q = v * (1f - s * f), t = v * (1f - s * (1f - f));
        return i switch
        {
            0 => new Vector3(v, t, p),
            1 => new Vector3(q, v, p),
            2 => new Vector3(p, v, t),
            3 => new Vector3(p, q, v),
            4 => new Vector3(t, p, v),
            _ => new Vector3(v, p, q),
        };
    }

    // ---- trees, headers, tabs, menus -------------------------------------------------------------

    /// <summary>
    /// A row of a tree. When it returns true the node is open: draw the children and call <see cref="TreePop"/>.
    /// <paramref name="clicked"/> reports a click on the label (for selection).
    /// </summary>
    public bool TreeNode(string label, bool selected, out bool clicked, bool leaf = false, IconKind icon = IconKind.None, bool defaultOpen = false)
    {
        var rect = AllocItem(0f, Theme.RowHeight);
        int id = GetId(label);
        if (!_open.TryGetValue(id, out bool open)) _open[id] = open = defaultOpen;

        var arrow = new UiRect(rect.X, rect.Y, 20f, rect.Height);
        bool onArrow = !leaf && IsHovered(arrow);
        clicked = ButtonBehavior(id, rect, out bool hovered, out _);
        LastItemDoubleClicked = hovered && Input.MouseDoubleClicked[0];
        if (clicked && onArrow)
        {
            _open[id] = open = !open;
            clicked = false;
        }
        else if (LastItemDoubleClicked && !leaf) _open[id] = open = !open;

        float hv = Fade(id, hovered);
        if (selected) FillRect(Canvas, rect, 4f, Theme.Selection);
        else if (hv > 0f) FillRect(Canvas, rect, 4f, WithAlpha(Theme.Hover, (byte)(Theme.Hover.Alpha * hv)));

        float x = rect.X + 20f;
        if (!leaf) Icons.Draw(Canvas, open ? IconKind.ChevronDown : IconKind.ChevronRight, new UiRect(rect.X + 5f, rect.Y + 7f, 10f, rect.Height - 14f), Theme.TextDim);
        if (icon != IconKind.None)
        {
            Icons.Draw(Canvas, icon, new UiRect(x, rect.Y + 4f, rect.Height - 8f, rect.Height - 8f), selected ? Theme.AccentHover : Theme.TextDim);
            x += rect.Height - 2f;
        }
        DrawText(Canvas, Visible(label), new UiRect(x, rect.Y, rect.Right - x - 4f, rect.Height), selected ? SKColors.White : Theme.Text, TextAlign.Left, Theme.FontSize);

        if (open && !leaf)
        {
            Indent(14f);
            return true;
        }
        return false;
    }

    public void TreePop() => Unindent(14f);

    /// <summary>A section header that folds its content. Returns true while open.</summary>
    public bool CollapsingHeader(string label, bool defaultOpen = true)
    {
        Spacing(2f);
        var rect = AllocItem(0f, 28f);
        int id = GetId(label);
        if (!_open.TryGetValue(id, out bool open)) _open[id] = open = defaultOpen;
        if (ButtonBehavior(id, rect, out bool hovered, out _)) _open[id] = open = !open;

        float hv = Fade(id, hovered);
        FillRect(Canvas, rect, 6f, Lerp(Theme.Header, Theme.HeaderHover, hv));
        Icons.Draw(Canvas, open ? IconKind.ChevronDown : IconKind.ChevronRight, new UiRect(rect.X + 9f, rect.Y + 9f, 10f, rect.Height - 18f), Theme.TextDim);
        DrawText(Canvas, Visible(label), new UiRect(rect.X + 28f, rect.Y, rect.Width - 34f, rect.Height), Theme.Text, TextAlign.Left, Theme.FontSize, FontWeight.SemiBold);
        return open;
    }

    /// <summary>A row of tabs. Returns the (possibly new) selected index.</summary>
    public int TabBar(string id, IReadOnlyList<string> labels, int selected, IReadOnlyList<IconKind>? icons = null, bool compact = false)
    {
        var bar = AllocItem(0f, 32f);
        float x = bar.X;
        PushId(id);
        for (int i = 0; i < labels.Count; i++)
        {
            string text = Visible(labels[i]);
            bool hasIcon = icons != null && i < icons.Count && icons[i] != IconKind.None;
            bool iconOnly = compact && hasIcon && i != selected;
            float w = iconOnly ? 38f : Fonts.Measure(text, Theme.FontSize, FontWeight.Medium) + 24f + (hasIcon ? 20f : 0f);
            var tab = new UiRect(x, bar.Y, w, bar.Height);
            int tid = GetId(labels[i]);
            bool clicked = ButtonBehavior(tid, tab, out bool hovered, out _);
            if (clicked) selected = i;
            float hv = Fade(tid, hovered);
            bool active = i == selected;
            float t = Fade(HashCode.Combine(tid, 7), active);

            var color = Lerp(Lerp(Theme.TextDim, Theme.Text, hv), SKColors.White, t);
            float tx = iconOnly ? tab.X + 12f : tab.X + 12f;
            if (hasIcon)
            {
                Icons.Draw(Canvas, icons![i], new UiRect(tx, tab.Y + 9f, 14f, tab.Height - 18f), active ? Theme.AccentHover : color);
                tx += 20f;
            }
            if (!iconOnly)
                DrawText(Canvas, text, new UiRect(tx, tab.Y, tab.Right - tx - 8f, tab.Height - 2f), color, TextAlign.Left, Theme.FontSize, FontWeight.Medium);
            else
            {
                _lastItemRect = tab;
                Tooltip(text);
            }
            if (t > 0f) FillRect(Canvas, new UiRect(tab.X + 8f, tab.Bottom - 3f, (tab.Width - 16f) * t, 2f), 1f, Theme.Accent);
            x += w;
        }
        PopId();
        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = 1f;
        _paint.Color = Theme.Separator;
        Canvas.DrawLine(bar.X, bar.Bottom - 0.5f, bar.Right, bar.Bottom - 0.5f, _paint);
        return selected;
    }

    /// <summary>An entry of a menu popup. Closes the popup when chosen.</summary>
    public bool MenuItem(string label, string? shortcut = null, bool enabled = true, IconKind icon = IconKind.None)
    {
        var rect = AllocItem(0f, Theme.RowHeight);
        int id = GetId(label);
        bool clicked = ButtonBehavior(id, rect, out bool hovered, out _);
        if (!enabled) clicked = false;
        float hv = Fade(id, hovered && enabled);
        if (hv > 0f) FillRect(Canvas, rect, 4f, WithAlpha(Theme.Accent, (byte)(150 * hv)));

        var color = enabled ? Theme.Text : Theme.TextDisabled;
        float x = rect.X + 8f;
        if (icon != IconKind.None)
        {
            Icons.Draw(Canvas, icon, new UiRect(x, rect.Y + 5f, rect.Height - 10f, rect.Height - 10f), enabled ? Theme.TextDim : Theme.TextDisabled);
            x += rect.Height;
        }
        DrawText(Canvas, Visible(label), new UiRect(x, rect.Y, rect.Width - 16f, rect.Height), color, TextAlign.Left, Theme.FontSize);
        if (shortcut != null) DrawText(Canvas, shortcut, new UiRect(rect.X, rect.Y, rect.Width - 10f, rect.Height), Theme.TextDim, TextAlign.Right, Theme.SmallFontSize);
        if (clicked) CloseCurrentPopup();
        return clicked;
    }

    /// <summary>A button that opens a menu popup below it; fill the menu between BeginMenu and EndMenu when it returns true.</summary>
    public bool BeginMenu(string label, float width = 190f)
    {
        string popup = "##menu" + label;
        string text = Visible(label);
        var rect = AllocItem(Fonts.Measure(text, Theme.FontSize, FontWeight.Medium) + 20f, Theme.ButtonHeight);
        int id = GetId(label);
        bool clicked = ButtonBehavior(id, rect, out bool hovered, out _);
        bool open = IsPopupOpen(popup);
        float hv = Fade(id, hovered || open);
        if (hv > 0f) FillRect(Canvas, rect, 5f, WithAlpha(Theme.ButtonHover, (byte)(255 * hv)));
        DrawText(Canvas, text, rect, Theme.Text, TextAlign.Center, Theme.FontSize, FontWeight.Medium);
        if (clicked) TogglePopup(popup, rect);
        return BeginPopup(popup, width, 400f);
    }

    public void EndMenu() => EndPopup();

    /// <summary>A thin vertical divider on the current line (toolbars).</summary>
    public void VerticalSeparator(float height = 0f)
    {
        var rect = AllocItem(9f, height > 0f ? height : Theme.ButtonHeight);
        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = 1f;
        _paint.Color = Theme.Separator;
        Canvas.DrawLine(rect.CenterX, rect.Y + 4f, rect.CenterX, rect.Bottom - 4f, _paint);
    }

    // ---- docking ---------------------------------------------------------------------------------

    /// <summary>
    /// A draggable bar between docked panels. <paramref name="size"/> is the size of the panel on one side; dragging
    /// moves it by the mouse movement times <paramref name="direction"/> (+1 if the panel grows towards +x/+y, -1 otherwise).
    /// </summary>
    public bool Splitter(string name, UiRect rect, bool vertical, ref float size, float min, float max, float direction)
    {
        bool changed = false;
        const WindowFlags flags = WindowFlags.Fixed | WindowFlags.NoBackground | WindowFlags.NoScroll;
        if (Begin("##split" + name, rect, flags))
        {
            int id = GetId(name);
            bool hovered = IsHovered(rect) && (_activeId == 0 || _activeId == id);
            if (hovered && Input.MousePressed[0])
            {
                _activeId = id;
                _dragStartX = vertical ? Input.MousePosition.X : Input.MousePosition.Y;
                _dragStartValue = size;
            }
            if (_activeId == id)
            {
                if (Input.MouseDown[0])
                {
                    float now = vertical ? Input.MousePosition.X : Input.MousePosition.Y;
                    float next = Math.Clamp(_dragStartValue + (now - _dragStartX) * direction, min, max);
                    changed = MathF.Abs(next - size) > 0.01f;
                    size = next;
                }
                else _activeId = 0;
            }

            bool lit = hovered || _activeId == id;
            if (lit) Cursor = vertical ? CursorShape.ResizeH : CursorShape.ResizeV;
            float t = Fade(id, lit);
            if (t > 0f)
            {
                var line = vertical ? new UiRect(rect.CenterX - 1f, rect.Y, 2f, rect.Height) : new UiRect(rect.X, rect.CenterY - 1f, rect.Width, 2f);
                FillRect(Canvas, line, 1f, WithAlpha(Theme.Accent, (byte)(220 * t)));
            }
        }
        End();
        return changed;
    }

    /// <summary>A small colour button (palettes). Returns true when clicked.</summary>
    public bool ColorSwatch(string id, OpenTK.Mathematics.Color4 color, float width, float height, bool selected)
    {
        var rect = AllocItem(width, height);
        int sid = GetId(id);
        bool clicked = ButtonBehavior(sid, rect, out bool hovered, out _);
        float hv = Fade(sid, hovered);
        FillRect(Canvas, rect, 5f, new SKColor(ToByte(color.R), ToByte(color.G), ToByte(color.B)));
        StrokeRect(Canvas, rect.Deflate(0.5f), 5f, selected ? SKColors.White : Lerp(Theme.Border, Theme.BorderHover, hv));
        if (selected) StrokeRect(Canvas, rect.Inflate(2f), 6f, Theme.Accent);
        return clicked;
    }
}
