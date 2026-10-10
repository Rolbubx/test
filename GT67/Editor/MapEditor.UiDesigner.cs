using GT67.Gui;
using GT67.UI;
using OpenTK.Mathematics;

namespace GT67.Editor;

// The in-editor designer for the game's user interface (Window.GameUi): add elements, place them in the viewport, set
// their look and what buttons do. The result is written into GeneratedGame (UiGenerator.cs) when you Compile.
public sealed partial class MapEditor
{
    private static readonly UiKind[] UiKinds = Enum.GetValues<UiKind>();

    private static IconKind IconFor(UiKind kind) => kind switch
    {
        UiKind.Panel => IconKind.Panel,
        UiKind.Label => IconKind.Text,
        UiKind.Button => IconKind.Button,
        UiKind.ProgressBar => IconKind.ProgressBar,
        UiKind.Slider => IconKind.Slider,
        UiKind.Checkbox => IconKind.Checkbox,
        UiKind.TextBox => IconKind.TextBox,
        _ => IconKind.Image,
    };

    private readonly List<(UiElement element, bool visible, bool enabled, string text, float value, bool isChecked)> _uiSnapshot = new();

    // moving and resizing in the viewport
    private UiElement? _uiDragElement;
    private bool _uiResizing;
    private Vector2 _uiDragMouse, _uiDragOffset, _uiDragSize;

    private UiCanvas Ui => _window.GameUi;

    /// <summary>The element being edited in the UI designer.</summary>
    private UiElement? SelectedUi { get; set; }

    private void SelectUi(UiElement? element)
    {
        SelectedUi = element;
        if (element != null)
        {
            _window.DebugUI.Selected = null;
            SelectedLight = null;
        }
    }

    // ---- play: keep the designed values ----------------------------------------------------------

    private void SnapshotUi()
    {
        _uiSnapshot.Clear();
        foreach (var e in Ui.Traverse()) _uiSnapshot.Add((e, e.Visible, e.Enabled, e.Text, e.Value, e.Checked));
    }

    private void RestoreUi()
    {
        foreach (var (e, visible, enabled, text, value, isChecked) in _uiSnapshot)
        {
            e.Visible = visible;
            e.Enabled = enabled;
            e.Text = text;
            e.Value = value;
            e.Checked = isChecked;
        }
        _uiSnapshot.Clear();
    }

    // ---- picking and dragging in the viewport ----------------------------------------------------

    private void BeginUiDrag()
    {
        var vp = _window.Viewport;
        var mouse = new Vector2(_window.MousePosition.X, _window.MousePosition.Y);

        if (SelectedUi is { } current && current.Anchor != UiAnchor.Stretch)
        {
            var r = Ui.RectOf(current, vp);
            if (new UiRect(r.Right - 9f, r.Bottom - 9f, 18f, 18f).Contains(mouse))
            {
                StartUiDrag(current, mouse, resize: true);
                return;
            }
        }

        var hit = Ui.HitTest(mouse, vp);
        SelectUi(hit);
        if (hit != null && hit.Anchor != UiAnchor.Stretch) StartUiDrag(hit, mouse, resize: false);
    }

    private void StartUiDrag(UiElement element, Vector2 mouse, bool resize)
    {
        _uiDragElement = element;
        _uiResizing = resize;
        _uiDragMouse = mouse;
        _uiDragOffset = element.Offset;
        _uiDragSize = element.Size;
    }

    private void UpdateUiDrag(bool leftDown)
    {
        if (_uiDragElement == null) return;
        if (!leftDown || Playing)
        {
            _uiDragElement = null;
            return;
        }

        var mouse = new Vector2(_window.MousePosition.X, _window.MousePosition.Y);
        var delta = (mouse - _uiDragMouse) / MathF.Max(0.01f, Ui.Scale);
        bool snap = Settings.SnapEnabled;
        if (_uiResizing)
        {
            var size = _uiDragSize + delta;
            _uiDragElement.Size = new Vector2(MathF.Max(8f, Round(size.X, snap)), MathF.Max(8f, Round(size.Y, snap)));
        }
        else
        {
            var offset = _uiDragOffset + delta;
            _uiDragElement.Offset = new Vector2(Round(offset.X, snap), Round(offset.Y, snap));
        }
    }

    private static float Round(float value, bool snap) => snap ? MathF.Round(value / 8f) * 8f : MathF.Round(value);

    /// <summary>Draws the selection frame of the element being edited over the game UI.</summary>
    private void DrawUiSelection()
    {
        if (!UiMode || Playing) return;
        var vp = _window.Viewport;
        var mouse = new Vector2(_window.MousePosition.X, _window.MousePosition.Y);

        if (vp.Contains(mouse) && !G.IsPointOverUi(mouse) && Ui.HitTest(mouse, vp) is { } hover && hover != SelectedUi)
            G.SelectionBox(Ui.RectOf(hover, vp), false);
        if (SelectedUi is { } selected && selected.IsShown)
            G.SelectionBox(Ui.RectOf(selected, vp), selected.Anchor != UiAnchor.Stretch);
    }

    // ---- adding and organising -------------------------------------------------------------------

    private UiElement AddUiElement(UiKind kind)
    {
        var parent = SelectedUi is { Kind: UiKind.Panel } panel ? panel : SelectedUi?.Parent;
        var element = new UiElement(kind);
        element.Name = Ui.UniqueName(element.Name);
        if (parent == null) element.Anchor = UiAnchor.Center;
        else element.Offset = new Vector2(12f, 12f);
        Ui.Add(element, parent);
        SelectUi(element);
        return element;
    }

    private void DuplicateUi(UiElement source)
    {
        var copy = source.Clone();
        RenameUnique(copy);
        copy.Offset += new Vector2(16f, 16f);
        Ui.Add(copy, source.Parent);
        SelectUi(copy);
    }

    private void RenameUnique(UiElement element)
    {
        element.Name = Ui.UniqueName(element.Name);
    }

    private void MoveUi(UiElement element, int direction)
    {
        var siblings = element.Parent?.Children ?? Ui.Roots;
        int index = siblings.ToList().IndexOf(element);
        int target = index + direction;
        if (index < 0 || target < 0 || target >= siblings.Count) return;
        Ui.Move(element, element.Parent, direction > 0 ? target + 1 : target);
    }

    private void AddHudPreset()
    {
        var panel = Ui.Add(new UiElement(UiKind.Panel)
        {
            Name = Ui.UniqueName("HUD"),
            Anchor = UiAnchor.TopLeft,
            Offset = new Vector2(20f, 20f),
            Size = new Vector2(300f, 92f),
        });
        Ui.Add(new UiElement(UiKind.Label) { Name = Ui.UniqueName("HealthLabel"), Text = "Health", Offset = new Vector2(16f, 10f), Size = new Vector2(120f, 24f), FontSize = 15f, Bold = true }, panel);
        Ui.Add(new UiElement(UiKind.ProgressBar) { Name = Ui.UniqueName("HealthBar"), Value = 0.75f, Offset = new Vector2(16f, 38f), Size = new Vector2(268f, 18f), Fill = new Color4(0.25f, 0.8f, 0.45f, 1f) }, panel);
        Ui.Add(new UiElement(UiKind.Label) { Name = Ui.UniqueName("Score"), Text = "Score 0", Offset = new Vector2(16f, 62f), Size = new Vector2(268f, 22f), FontSize = 14f, TextColor = new Color4(0.8f, 0.84f, 0.92f, 1f) }, panel);
        SelectUi(panel);
        Say("Added a HUD: set the bar from a script with Game.Ui.Find(\"HealthBar\").Value = 0.5f.");
    }

    private void AddMenuPreset()
    {
        var panel = Ui.Add(new UiElement(UiKind.Panel)
        {
            Name = Ui.UniqueName("Menu"),
            Anchor = UiAnchor.Center,
            Size = new Vector2(340f, 250f),
        });
        Ui.Add(new UiElement(UiKind.Label) { Name = Ui.UniqueName("MenuTitle"), Text = "Paused", Anchor = UiAnchor.Top, Offset = new Vector2(0f, 20f), Size = new Vector2(300f, 36f), FontSize = 26f, Bold = true, Align = TextAlign.Center }, panel);
        Ui.Add(new UiElement(UiKind.Button) { Name = Ui.UniqueName("ResumeButton"), Text = "Resume", Anchor = UiAnchor.Center, Offset = new Vector2(0f, -10f), Size = new Vector2(240f, 46f), Action = $"hide:{panel.Name}" }, panel);
        Ui.Add(new UiElement(UiKind.Button) { Name = Ui.UniqueName("QuitButton"), Text = "Quit", Anchor = UiAnchor.Center, Offset = new Vector2(0f, 50f), Size = new Vector2(240f, 46f), Background = new Color4(0.85f, 0.3f, 0.3f, 1f), Action = "quit" }, panel);
        SelectUi(panel);
        Say("Added a menu panel. Show it with a button whose Action is show:" + panel.Name + ", or from a script.");
    }

    // ---- left tab: the element tree --------------------------------------------------------------

    private void DrawUiTree()
    {
        var g = G;
        g.SeparatorText("Add");
        foreach (var kind in UiKinds)
        {
            if (kind != UiKinds[0]) g.SameLine();
            if (g.IconButton(IconFor(kind), "add" + kind, false, 34f)) AddUiElement(kind);
            g.Tooltip("Add " + kind);
        }

        g.Spacing(2f);
        if (g.Button("HUD preset")) AddHudPreset();
        g.SameLine();
        if (g.Button("Menu preset")) AddMenuPreset();

        g.SeparatorText("Elements");
        if (Ui.Roots.Count == 0)
        {
            g.TextWrapped("The game UI is empty. Add elements with the buttons above, then place them in the viewport.", dim: true);
        }
        foreach (var root in Ui.Roots.ToArray()) DrawUiNode(root);

        if (SelectedUi is { } selected)
        {
            g.Spacing(4f);
            if (g.IconButton(IconKind.ArrowUp, "uiup")) MoveUi(selected, -1);
            g.Tooltip("Draw earlier (behind)");
            g.SameLine();
            if (g.IconButton(IconKind.ArrowDown, "uidown")) MoveUi(selected, 1);
            g.Tooltip("Draw later (in front)");
            g.SameLine();
            if (g.IconButton(IconKind.Copy, "uidup")) DuplicateUi(selected);
            g.Tooltip("Duplicate");
            g.SameLine();
            if (g.IconButton(IconKind.Trash, "uidel"))
            {
                Ui.Remove(selected);
                SelectUi(null);
            }
            g.Tooltip("Delete (Del)");
        }

        g.SeparatorText("Canvas");
        g.Slider("UI scale", Ui.Scale, 0.5f, 2.5f, v => Ui.Scale = v);
        g.TextWrapped("Scripts reach elements by name: Game.Ui.Find(\"Score\").Text = \"12\". Buttons can run actions: quit, show:Name, hide:Name, toggle:Name, cursor:on, cursor:off, or your own text.", dim: true);
    }

    private void DrawUiNode(UiElement element)
    {
        var g = G;
        bool open = g.TreeNode($"{element.Name}##ui{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(element)}", element == SelectedUi,
            out bool clicked, element.Children.Count == 0, IconFor(element.Kind), defaultOpen: true);
        if (clicked) SelectUi(element);
        if (!open) return;

        foreach (var child in element.Children.ToArray()) DrawUiNode(child);
        g.TreePop();
    }

    // ---- right tab: properties -------------------------------------------------------------------

    private void DrawUiProperties()
    {
        var g = G;
        if (SelectedUi is not { } e)
        {
            g.Spacing(10f);
            g.TextDim("No UI element selected.");
            g.TextWrapped("Pick one in the Elements list (left, UI tab) or click it in the viewport.", dim: true);
            return;
        }

        g.Heading(e.Name);
        g.TextDim(e.Kind.ToString());
        g.Input("Name", e.Name, v => e.Name = v);
        g.Check("Visible", e.Visible, v => e.Visible = v);
        if (e.IsInteractive) g.Check("Enabled", e.Enabled, v => e.Enabled = v);

        g.SeparatorText("Layout");
        g.Choose("Anchor", e.Anchor, v => e.Anchor = v);
        var offset = e.Offset;
        if (g.DragVector2(e.Anchor == UiAnchor.Stretch ? "Margin" : "Offset", ref offset, 1f)) e.Offset = offset;
        if (e.Anchor != UiAnchor.Stretch)
        {
            var size = e.Size;
            if (g.DragVector2("Size", ref size, 1f)) e.Size = new Vector2(MathF.Max(1f, size.X), MathF.Max(1f, size.Y));
        }

        switch (e.Kind)
        {
            case UiKind.Panel:
                DrawBox(e);
                break;

            case UiKind.Label:
                g.SeparatorText("Text");
                DrawTextProperties(e);
                g.Rgba("Background", e.Background, v => e.Background = v);
                g.Slider("Corner radius", e.CornerRadius, 0f, 40f, v => e.CornerRadius = v, "0");
                break;

            case UiKind.Button:
                g.SeparatorText("Text");
                DrawTextProperties(e);
                g.Rgba("Colour", e.Background, v => e.Background = v);
                g.Slider("Corner radius", e.CornerRadius, 0f, 40f, v => e.CornerRadius = v, "0");
                g.SeparatorText("Action");
                g.Input("On click", e.Action, v => e.Action = v, "quit, show:Name, hide:Name...");
                g.TextWrapped("quit | show:Name | hide:Name | toggle:Name | cursor:on | cursor:off | anything else is sent to your scripts (Game.Ui.ActionTriggered).", dim: true);
                break;

            case UiKind.ProgressBar:
                g.SeparatorText("Value");
                DrawRange(e);
                g.Rgba("Fill", e.Fill, v => e.Fill = v);
                g.Rgba("Background", e.Background, v => e.Background = v);
                g.Slider("Corner radius", e.CornerRadius, 0f, 40f, v => e.CornerRadius = v, "0");
                g.Input("Text", e.Text, v => e.Text = v, "optional label");
                break;

            case UiKind.Slider:
                g.SeparatorText("Value");
                DrawRange(e);
                g.Rgba("Fill", e.Fill, v => e.Fill = v);
                break;

            case UiKind.Checkbox:
                g.SeparatorText("Text");
                DrawTextProperties(e);
                g.Check("Checked", e.Checked, v => e.Checked = v);
                break;

            case UiKind.TextBox:
                g.SeparatorText("Text");
                g.Input("Text", e.Text, v => e.Text = v);
                g.Input("Hint", e.Hint, v => e.Hint = v);
                g.Slider("Font size", e.FontSize, 8f, 64f, v => e.FontSize = v, "0");
                break;

            case UiKind.Image:
                g.SeparatorText("Image");
                g.Input("File", e.ImagePath, v => e.ImagePath = v, "drop a .png / .jpg here");
                g.Rgba("Tint", e.Tint, v => e.Tint = v);
                g.Slider("Corner radius", e.CornerRadius, 0f, 64f, v => e.CornerRadius = v, "0");
                g.TextWrapped("Drop an image file onto the window to use it. It is copied into Assets/UI when you Compile.", dim: true);
                break;
        }

    }

    private void DrawBox(UiElement e)
    {
        var g = G;
        g.SeparatorText("Look");
        g.Rgba("Background", e.Background, v => e.Background = v);
        g.Rgba("Border colour", e.BorderColor, v => e.BorderColor = v);
        g.Slider("Border width", e.BorderWidth, 0f, 8f, v => e.BorderWidth = v, "0.0");
        g.Slider("Corner radius", e.CornerRadius, 0f, 48f, v => e.CornerRadius = v, "0");
    }

    private void DrawTextProperties(UiElement e)
    {
        var g = G;
        g.Input("Text", e.Text, v => e.Text = v);
        g.Slider("Font size", e.FontSize, 8f, 96f, v => e.FontSize = v, "0");
        g.Check("Bold", e.Bold, v => e.Bold = v);
        g.Choose("Align", e.Align, v => e.Align = v);
        g.Rgba("Text colour", e.TextColor, v => e.TextColor = v);
    }

    private void DrawRange(UiElement e)
    {
        var g = G;
        g.Slider("Value", e.Value, e.Min, MathF.Max(e.Max, e.Min + 0.0001f), v => e.Value = v);
        g.Drag("Min", e.Min, 0.1f, v => e.Min = v);
        g.Drag("Max", e.Max, 0.1f, v => e.Max = v);
    }

    /// <summary>An image file dropped onto the window while designing: becomes (or fills) an Image element.</summary>
    private bool TryDropImage(string file)
    {
        string extension = Path.GetExtension(file).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".gif")) return false;

        var target = SelectedUi is { Kind: UiKind.Image } image ? image : AddUiElement(UiKind.Image);
        target.ImagePath = file;
        Say($"UI image set to {Path.GetFileName(file)}.");
        return true;
    }
}
