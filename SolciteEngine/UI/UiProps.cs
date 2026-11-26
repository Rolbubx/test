using OpenTK.Mathematics;

namespace SolciteEngine.UI;

/// <summary>
/// A modifier added to a UI element's <see cref="UiElement.Props"/> that changes how it looks, e.g. rounded corners
/// (<see cref="CornerProp"/>) or a colour gradient (<see cref="GradientProp"/>):
/// <c>new Frame { Props = { new CornerProp(16f), new GradientProp(Color4.Orange, Color4.Purple, 90f) } }</c>.
/// </summary>
public abstract class UiProp
{
    /// <summary>A copy of this prop (used when an element is duplicated).</summary>
    public UiProp Clone() => (UiProp)MemberwiseClone();
}

/// <summary>Rounds the corners of the element it is added to (frame, label, button, image, shape). Overrides <see cref="UiElement.CornerRadius"/>.</summary>
public sealed class CornerProp : UiProp
{
    public CornerProp() { }

    public CornerProp(float radius) => Radius = radius;

    /// <summary>Radius of the corners in pixels (before <see cref="UiCanvas.Scale"/>). A radius of half the height gives a pill.</summary>
    public float Radius { get; set; } = 8f;
}

/// <summary>
/// Fills the background of the element it is added to with a linear gradient from <see cref="From"/> to <see cref="To"/>
/// instead of the flat <see cref="UiElement.Background"/> (for a shape: instead of its <see cref="UiElement.Fill"/>).
/// </summary>
public sealed class GradientProp : UiProp
{
    public GradientProp() { }

    public GradientProp(Color4 from, Color4 to, float rotation = 0f)
    {
        From = from;
        To = to;
        Rotation = rotation;
    }

    /// <summary>Colour at the start of the gradient.</summary>
    public Color4 From { get; set; } = new(0.30f, 0.55f, 1f, 1f);

    /// <summary>Colour at the end of the gradient.</summary>
    public Color4 To { get; set; } = new(0.62f, 0.30f, 1f, 1f);

    /// <summary>Direction in degrees: 0 = left to right, 90 = top to bottom.</summary>
    public float Rotation { get; set; }
}
