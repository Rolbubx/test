using OpenTK.Mathematics;

namespace SolciteEngine.UI;

// Typed UI elements for scripts: each is a UiElement of one kind with a readable name, so a script can write
//   var hud = Game.Ui.Add(new Frame { Size = new Vector2(300f, 90f), Props = { new CornerProp(14f) } });
//   Game.Ui.Add(new TextLabel("Score 0") { Name = "Score" }, hud);
// and find them back with Game.Ui.Find<TextLabel>("Score").

/// <summary>A box that holds other elements (a panel): background, border, corners. Add <see cref="CornerProp"/> / <see cref="GradientProp"/> to style it.</summary>
public sealed class Frame : UiElement
{
    public Frame() : base(UiKind.Panel) { }
}

/// <summary>A piece of text.</summary>
public sealed class TextLabel : UiElement
{
    public TextLabel() : base(UiKind.Label) { }

    public TextLabel(string text) : this() => Text = text;
}

/// <summary>A clickable button with text. React with <see cref="UiElement.Clicked"/> or an <see cref="UiElement.Action"/>.</summary>
public sealed class TextButton : UiElement
{
    public TextButton() : base(UiKind.Button) { }

    public TextButton(string text) : this() => Text = text;
}

/// <summary>A picture (.png / .jpg...) from <see cref="UiElement.ImagePath"/>, tinted by <see cref="UiElement.Tint"/>.</summary>
public sealed class Image : UiElement
{
    public Image() : base(UiKind.Image) { }

    public Image(string path) : this() => ImagePath = path;
}

/// <summary>A clickable picture: an <see cref="Image"/> that works like a <see cref="TextButton"/> (Clicked, Action).</summary>
public sealed class ImageButton : UiElement
{
    public ImageButton() : base(UiKind.ImageButton) { }

    public ImageButton(string path) : this() => ImagePath = path;
}

/// <summary>
/// A simple shape: a line, rectangle, ellipse or triangle in the colour <see cref="UiElement.Fill"/>. A line goes from
/// <see cref="UiElement.LineStart"/> to <see cref="UiElement.LineEnd"/> (fractions of the element's box); the easiest way
/// to make one is <see cref="Line(Vector2, Vector2, float)"/>.
/// </summary>
public sealed class BasicObject : UiElement
{
    public BasicObject() : base(UiKind.Shape) { }

    public BasicObject(UiShape shape) : this() => Shape = shape;

    /// <summary>A line from <paramref name="from"/> to <paramref name="to"/>, in pixels from the parent's top-left corner.</summary>
    public static BasicObject Line(Vector2 from, Vector2 to, float thickness = 2f)
    {
        var line = new BasicObject(UiShape.Line) { Thickness = thickness };
        line.SetLine(from, to);
        return line;
    }

    /// <summary>Moves this line so it goes from <paramref name="from"/> to <paramref name="to"/> (pixels from the parent's top-left corner).</summary>
    public void SetLine(Vector2 from, Vector2 to)
    {
        float pad = MathF.Max(1f, Thickness * 0.5f);
        var min = Vector2.ComponentMin(from, to) - new Vector2(pad);
        var size = Vector2.ComponentMax(from, to) + new Vector2(pad) - min;
        Anchor = UiAnchor.TopLeft;
        Offset = min;
        Size = size;
        LineStart = (from - min) / size;
        LineEnd = (to - min) / size;
    }
}
