using SolciteEngine.Core;
using SolciteEngine.Gui;
using OpenTK.Mathematics;
using SkiaSharp;

namespace SolciteEngine.UI;

/// <summary>
/// The game's user interface: a tree of <see cref="UiElement"/>s drawn over the 3D view with the same Skia toolkit as
/// the editor. Built in the editor's UI designer (and written into GeneratedGame), or in code, and found by name from
/// scripts: <c>Game.Ui.Find("Score").Text = "12"</c>.
/// </summary>
public sealed class UiCanvas : IDisposable
{
    private readonly List<UiElement> _roots = new();
    private readonly Dictionary<string, SKImage?> _images = new();

    /// <summary>Multiplies sizes, offsets and font sizes (make the whole UI bigger or smaller).</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>Set by actions such as "cursor:on": the window frees the mouse so menus can be clicked.</summary>
    public bool ShowCursor { get; set; }

    public IReadOnlyList<UiElement> Roots => _roots;

    /// <summary>A button with a custom action (anything but quit/show/hide/toggle/cursor) was clicked.</summary>
    public event Action<UiElement>? ActionTriggered;

    /// <summary>A button with the action "quit" was clicked.</summary>
    public event Action? QuitRequested;

    // ---- the tree --------------------------------------------------------------------------------

    /// <summary>Adds <paramref name="element"/> at the top level or inside <paramref name="parent"/> and returns it as its own type: <c>var play = Game.Ui.Add(new TextButton("Play"))</c>.</summary>
    public T Add<T>(T element, UiElement? parent = null) where T : UiElement
    {
        if (parent != null) parent.Add(element);
        else
        {
            element.Parent?.Remove(element);
            _roots.Add(element);
        }
        return element;
    }

    public void Remove(UiElement element)
    {
        if (element.Parent != null) element.Parent.Remove(element);
        else _roots.Remove(element);
    }

    public void Clear() => _roots.Clear();

    public IEnumerable<UiElement> Traverse()
    {
        foreach (var root in _roots)
            foreach (var e in Walk(root)) yield return e;
    }

    private static IEnumerable<UiElement> Walk(UiElement e)
    {
        yield return e;
        foreach (var child in e.Children.ToArray())
            foreach (var d in Walk(child)) yield return d;
    }

    public UiElement? Find(string name) => Traverse().FirstOrDefault(e => e.Name == name);

    /// <summary>The element called <paramref name="name"/> if it is a <typeparamref name="T"/>: <c>Game.Ui.Find&lt;TextLabel&gt;("Score")</c>.</summary>
    public T? Find<T>(string name) where T : UiElement => Traverse().OfType<T>().FirstOrDefault(e => e.Name == name);


    public string UniqueName(string baseName)
    {
        var used = new HashSet<string>(Traverse().Select(e => e.Name));
        if (!used.Contains(baseName)) return baseName;
        for (int i = 2; ; i++)
            if (!used.Contains($"{baseName} {i}")) return $"{baseName} {i}";
    }

    /// <summary>Moves <paramref name="element"/> under <paramref name="newParent"/> (null = top level) at <paramref name="index"/>.</summary>
    public void Move(UiElement element, UiElement? newParent, int index)
    {
        for (var p = newParent; p != null; p = p.Parent)
            if (p == element) return; // cannot go inside itself

        var siblings = element.Parent?.Children ?? _roots;
        int old = siblings.ToList().IndexOf(element);
        if (element.Parent == newParent && old >= 0 && old < index) index--;

        Remove(element);
        if (newParent != null)
        {
            newParent.Add(element);
            newParent.MoveChild(element, index);
        }
        else
        {
            _roots.Insert(Math.Clamp(index, 0, _roots.Count), element);
        }
    }

    // ---- layout ----------------------------------------------------------------------------------

    /// <summary>The rectangle of <paramref name="e"/> given the rectangle of its parent (or the screen).</summary>
    public UiRect Resolve(UiElement e, UiRect parent)
    {
        float s = Scale;
        if (e.Anchor == UiAnchor.Stretch)
            return new UiRect(parent.X + e.Offset.X * s, parent.Y + e.Offset.Y * s,
                MathF.Max(0f, parent.Width - 2f * e.Offset.X * s), MathF.Max(0f, parent.Height - 2f * e.Offset.Y * s));

        float w = e.Size.X * s, h = e.Size.Y * s;
        float x = e.Anchor switch
        {
            UiAnchor.TopLeft or UiAnchor.Left or UiAnchor.BottomLeft => parent.X,
            UiAnchor.Top or UiAnchor.Center or UiAnchor.Bottom => parent.CenterX - w * 0.5f,
            _ => parent.Right - w,
        };
        float y = e.Anchor switch
        {
            UiAnchor.TopLeft or UiAnchor.Top or UiAnchor.TopRight => parent.Y,
            UiAnchor.Left or UiAnchor.Center or UiAnchor.Right => parent.CenterY - h * 0.5f,
            _ => parent.Bottom - h,
        };
        return new UiRect(x + e.Offset.X * s, y + e.Offset.Y * s, w, h);
    }

    /// <summary>The on-screen rectangle of an element inside a canvas drawn in <paramref name="screen"/>.</summary>
    public UiRect RectOf(UiElement e, UiRect screen)
    {
        var chain = new Stack<UiElement>();
        for (var p = e; p != null; p = p.Parent) chain.Push(p);
        var rect = screen;
        while (chain.Count > 0) rect = Resolve(chain.Pop(), rect);
        return rect;
    }

    /// <summary>The top-most shown element under <paramref name="point"/> (for picking in the designer).</summary>
    public UiElement? HitTest(Vector2 point, UiRect screen)
    {
        for (int i = _roots.Count - 1; i >= 0; i--)
        {
            var hit = HitTest(_roots[i], point, screen);
            if (hit != null) return hit;
        }
        return null;
    }

    private UiElement? HitTest(UiElement e, Vector2 point, UiRect parent)
    {
        if (!e.Visible) return null;
        var rect = Resolve(e, parent);
        for (int i = e.Children.Count - 1; i >= 0; i--)
        {
            var hit = HitTest(e.Children[i], point, rect);
            if (hit != null) return hit;
        }
        return rect.Contains(point) ? e : null;
    }

    // ---- drawing ---------------------------------------------------------------------------------

    /// <summary>
    /// Draws the whole UI inside <paramref name="screen"/>. With <paramref name="interactive"/> off (the designer) the
    /// elements are only drawn, so clicks can select them instead.
    /// </summary>
    public void Draw(Gui.Gui g, UiRect screen, bool interactive)
    {
        const WindowFlags flags = WindowFlags.Fixed | WindowFlags.NoBackground | WindowFlags.NoScroll | WindowFlags.ClickThrough;
        if (g.Begin("##gameui", screen, flags))
        {
            foreach (var root in _roots.ToArray()) DrawElement(g, root, screen, interactive);
        }
        g.End();
    }

    private void DrawElement(Gui.Gui g, UiElement e, UiRect parent, bool interactive)
    {
        if (!e.Visible) return;
        var rect = Resolve(e, parent);
        float s = Scale;
        float font = e.FontSize * s;
        var weight = e.Bold ? FontWeight.SemiBold : FontWeight.Regular;
        var text = ToSk(e.TextColor);
        var back = ToSk(e.Background);
        float radius = e.EffectiveCornerRadius * s;
        var backFill = BackFill(e, back);
        bool live = interactive && e.Enabled;

        g.PushId(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(e));
        switch (e.Kind)
        {
            case UiKind.Panel:
                g.PanelRect(rect, backFill, radius, ToSk(e.BorderColor), e.BorderWidth);
                break;

            case UiKind.Label:
                if (backFill.IsVisible) g.PanelRect(rect, backFill, radius, SKColors.Transparent);
                g.LabelRect(rect, e.Text, text, font, weight, e.Align);
                break;

            case UiKind.Button:
                if (live)
                {
                    if (g.ButtonRect(rect, "button", e.Text, backFill, text, radius, font)) Click(e);
                }
                else
                {
                    g.PanelRect(rect, e.Enabled ? backFill : backFill with { From = Dim(backFill.From), To = Dim(backFill.To) }, radius, SKColors.Transparent);
                    g.LabelRect(rect, e.Text, text, font, FontWeight.Medium, TextAlign.Center);
                }
                break;

            case UiKind.ProgressBar:
                g.ProgressRect(rect, Fraction(e), ToSk(e.Fill), back, radius);
                if (e.Text.Length > 0) g.LabelRect(rect, e.Text, text, font, FontWeight.Medium, TextAlign.Center);
                break;

            case UiKind.Slider:
                if (live)
                {
                    float value = e.Value;
                    if (g.SliderRect(rect, "slider", ref value, e.Min, e.Max))
                    {
                        e.Value = value;
                        e.RaiseChanged();
                    }
                }
                else
                {
                    g.PanelRect(rect, g.Theme.Frame, 5f, g.Theme.Border, 1f);
                    g.ProgressRect(rect, Fraction(e), ToSk(e.Fill), g.Theme.Frame, 5f);
                }
                break;

            case UiKind.Checkbox:
                if (live)
                {
                    bool on = e.Checked;
                    if (g.CheckboxRect(rect, "check", e.Text, ref on, text, font))
                    {
                        e.Checked = on;
                        e.RaiseChanged();
                    }
                }
                else
                {
                    float box = MathF.Min(rect.Height - 4f, 22f);
                    g.PanelRect(new UiRect(rect.X, rect.CenterY - box * 0.5f, box, box), e.Checked ? g.Theme.Accent : g.Theme.Frame, 4f, g.Theme.Border, 1f);
                    g.LabelRect(new UiRect(rect.X + box + 8f, rect.Y, rect.Width - box - 8f, rect.Height), e.Text, text, font, weight, TextAlign.Left);
                }
                break;

            case UiKind.TextBox:
                if (live)
                {
                    string value = e.Text;
                    if (g.TextBoxRect(rect, "textbox", ref value, e.Hint))
                    {
                        e.Text = value;
                        e.RaiseChanged();
                    }
                }
                else
                {
                    g.PanelRect(rect, g.Theme.Frame, 5f, g.Theme.Border, 1f);
                    bool empty = e.Text.Length == 0;
                    g.LabelRect(rect.Deflate(8f, 0f), empty ? e.Hint : e.Text, empty ? g.Theme.TextDisabled : g.Theme.Text, font, FontWeight.Regular, TextAlign.Left);
                }
                break;

            case UiKind.Image:
                if (backFill.IsVisible) g.PanelRect(rect, backFill, radius, SKColors.Transparent);
                g.ImageRect(rect, GetImage(e.ImagePath), ToSk(e.Tint), radius);
                break;

            case UiKind.ImageButton:
                if (g.ImageButtonRect(rect, "imagebutton", GetImage(e.ImagePath), ToSk(e.Tint), backFill, radius, live) && live) Click(e);
                break;

            case UiKind.Shape:
                DrawShape(g, e, rect, s);
                break;
        }

        foreach (var child in e.Children.ToArray()) DrawElement(g, child, rect, interactive);
        g.PopId();
    }

    /// <summary>The background of <paramref name="e"/>: its <see cref="GradientProp"/> if it has one, else <paramref name="back"/>.</summary>
    private static UiFill BackFill(UiElement e, SKColor back) =>
        e.GetProp<GradientProp>() is { } gradient
            ? UiFill.Gradient(ToSk(gradient.From), ToSk(gradient.To), gradient.Rotation)
            : UiFill.Solid(back);

    private static void DrawShape(Gui.Gui g, UiElement e, UiRect rect, float scale)
    {
        // a shape is drawn in its Fill colour (or its gradient)
        var fill = BackFill(e, ToSk(e.Fill));
        float thickness = e.Thickness * scale;
        switch (e.Shape)
        {
            case UiShape.Line:
                var a = new SKPoint(rect.X + e.LineStart.X * rect.Width, rect.Y + e.LineStart.Y * rect.Height);
                var b = new SKPoint(rect.X + e.LineEnd.X * rect.Width, rect.Y + e.LineEnd.Y * rect.Height);
                g.LineRect(rect, a, b, fill.From, thickness);
                break;
            case UiShape.Rectangle:
                g.BoxRect(rect, fill, e.EffectiveCornerRadius * scale, e.Filled, thickness);
                break;
            case UiShape.Ellipse:
                g.EllipseRect(rect, fill, e.Filled, thickness);
                break;
            case UiShape.Triangle:
                g.TriangleRect(rect, fill, e.Filled, thickness);
                break;
        }
    }

    private static float Fraction(UiElement e) => e.Max > e.Min ? (e.Value - e.Min) / (e.Max - e.Min) : 0f;

    private static SKColor Dim(SKColor c) => new(c.Red, c.Green, c.Blue, (byte)(c.Alpha / 2));

    public static SKColor ToSk(Color4 c) => new(ToByte(c.R), ToByte(c.G), ToByte(c.B), ToByte(c.A));

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    // ---- actions ---------------------------------------------------------------------------------

    private void Click(UiElement e)
    {
        e.RaiseClicked();
        string action = e.Action.Trim();
        if (action.Length == 0) return;

        if (action == "quit")
        {
            QuitRequested?.Invoke();
            return;
        }

        int colon = action.IndexOf(':');
        if (colon > 0)
        {
            string verb = action[..colon], argument = action[(colon + 1)..].Trim();
            switch (verb)
            {
                case "show" or "hide" or "toggle":
                    if (Find(argument) is { } target)
                        target.Visible = verb == "show" || (verb == "toggle" && !target.Visible);
                    return;
                case "cursor":
                    ShowCursor = argument == "on";
                    return;
            }
        }
        ActionTriggered?.Invoke(e);
    }

    // ---- images ----------------------------------------------------------------------------------

    private SKImage? GetImage(string path)
    {
        if (path.Length == 0) return null;
        if (_images.TryGetValue(path, out var cached)) return cached;

        SKImage? image = null;
        string full = ModelCache.ResolvePath(path);
        try
        {
            if (File.Exists(full)) image = SKImage.FromEncodedData(full);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            image = null;
        }
        _images[path] = image;
        return image;
    }

    public void Dispose()
    {
        foreach (var image in _images.Values) image?.Dispose();
        _images.Clear();
    }
}
