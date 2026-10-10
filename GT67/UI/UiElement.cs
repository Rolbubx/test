using GT67.Gui;
using OpenTK.Mathematics;

namespace GT67.UI;

public enum UiKind
{
    Panel,
    Label,
    Button,
    ProgressBar,
    Slider,
    Checkbox,
    TextBox,
    Image,
}

/// <summary>Which point of the parent an element is attached to.</summary>
public enum UiAnchor
{
    TopLeft,
    Top,
    TopRight,
    Left,
    Center,
    Right,
    BottomLeft,
    Bottom,
    BottomRight,

    /// <summary>Fills the parent; <see cref="UiElement.Offset"/> is the margin on each side.</summary>
    Stretch,
}

/// <summary>
/// One element of a game UI (see <see cref="UiCanvas"/>): a panel, label, button, progress bar, slider, check box,
/// text box or image. Elements nest, are placed relative to an anchor of their parent, and are found by name:
/// <c>Game.Ui.Find("Health")</c>.
/// </summary>
public sealed class UiElement
{
    private readonly List<UiElement> _children = new();

    public UiElement(UiKind kind)
    {
        Kind = kind;
        ApplyDefaults();
    }

    public string Name { get; set; } = "";
    public UiKind Kind { get; }

    // placement
    public UiAnchor Anchor { get; set; } = UiAnchor.TopLeft;
    public Vector2 Offset { get; set; }
    public Vector2 Size { get; set; } = new(160f, 40f);

    // look
    public string Text { get; set; } = "";
    public string Hint { get; set; } = "";
    public float FontSize { get; set; } = 16f;
    public bool Bold { get; set; }
    public TextAlign Align { get; set; } = TextAlign.Left;
    public Color4 TextColor { get; set; } = Color4.White;
    public Color4 Background { get; set; } = new(0f, 0f, 0f, 0f);
    public Color4 Fill { get; set; } = new(0.30f, 0.55f, 1f, 1f);
    public Color4 BorderColor { get; set; } = new(1f, 1f, 1f, 0.1f);
    public float BorderWidth { get; set; }
    public float CornerRadius { get; set; } = 8f;
    public string ImagePath { get; set; } = "";
    public Color4 Tint { get; set; } = Color4.White;

    // behaviour
    public float Value { get; set; }
    public float Min { get; set; }
    public float Max { get; set; } = 1f;
    public bool Checked { get; set; }
    public bool Visible { get; set; } = true;
    public bool Enabled { get; set; } = true;

    /// <summary>What a button does: quit, show:Name, hide:Name, toggle:Name, cursor:on, cursor:off, or any text your script reacts to.</summary>
    public string Action { get; set; } = "";

    public UiElement? Parent { get; private set; }
    public IReadOnlyList<UiElement> Children => _children;

    /// <summary>Raised when a button is clicked.</summary>
    public event Action<UiElement>? Clicked;

    /// <summary>Raised when the value of a slider, check box or text box was changed by the user.</summary>
    public event Action<UiElement>? Changed;

    public bool IsInteractive => Kind is UiKind.Button or UiKind.Slider or UiKind.Checkbox or UiKind.TextBox;

    internal void RaiseClicked() => Clicked?.Invoke(this);
    internal void RaiseChanged() => Changed?.Invoke(this);

    public UiElement Add(UiElement child)
    {
        child.Parent?._children.Remove(child);
        child.Parent = this;
        _children.Add(child);
        return child;
    }

    public void Remove(UiElement child)
    {
        if (_children.Remove(child)) child.Parent = null;
    }

    /// <summary>Moves the element to another place among its siblings.</summary>
    internal void MoveChild(UiElement child, int newIndex)
    {
        int old = _children.IndexOf(child);
        if (old < 0) return;
        _children.RemoveAt(old);
        _children.Insert(Math.Clamp(newIndex, 0, _children.Count), child);
    }

    /// <summary>A copy of this element with copies of all its children (not attached to any parent).</summary>
    public UiElement Clone()
    {
        var copy = new UiElement(Kind)
        {
            Name = Name, Anchor = Anchor, Offset = Offset, Size = Size, Text = Text, Hint = Hint, FontSize = FontSize,
            Bold = Bold, Align = Align, TextColor = TextColor, Background = Background, Fill = Fill, BorderColor = BorderColor,
            BorderWidth = BorderWidth, CornerRadius = CornerRadius, ImagePath = ImagePath, Tint = Tint, Value = Value,
            Min = Min, Max = Max, Checked = Checked, Visible = Visible, Enabled = Enabled, Action = Action,
        };
        foreach (var child in _children) copy.Add(child.Clone());
        return copy;
    }

    /// <summary>True if this element and all its ancestors are visible.</summary>
    public bool IsShown => Visible && (Parent == null || Parent.IsShown);

    private void ApplyDefaults()
    {
        switch (Kind)
        {
            case UiKind.Panel:
                Size = new Vector2(300f, 200f);
                Background = new Color4(0.09f, 0.10f, 0.12f, 0.88f);
                BorderWidth = 1f;
                CornerRadius = 12f;
                break;
            case UiKind.Label:
                Text = "Label";
                Size = new Vector2(200f, 28f);
                FontSize = 18f;
                break;
            case UiKind.Button:
                Text = "Button";
                Size = new Vector2(180f, 44f);
                Background = new Color4(0.30f, 0.55f, 1f, 1f);
                Align = TextAlign.Center;
                Bold = true;
                break;
            case UiKind.ProgressBar:
                Size = new Vector2(260f, 18f);
                Background = new Color4(0.06f, 0.07f, 0.09f, 0.9f);
                Value = 0.6f;
                CornerRadius = 9f;
                break;
            case UiKind.Slider:
                Size = new Vector2(260f, 26f);
                Value = 0.5f;
                break;
            case UiKind.Checkbox:
                Text = "Option";
                Size = new Vector2(220f, 28f);
                break;
            case UiKind.TextBox:
                Hint = "Type here...";
                Size = new Vector2(260f, 38f);
                break;
            case UiKind.Image:
                Size = new Vector2(128f, 128f);
                CornerRadius = 0f;
                break;
        }
        Name = Kind.ToString();
    }
}
