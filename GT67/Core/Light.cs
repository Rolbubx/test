using OpenTK.Mathematics;

namespace GT67.Core;

public abstract class Light
{
    public string Name { get; set; } = "Light";
    public bool Enabled { get; set; } = true;

    /// <summary>sRGB colour; converted to linear when uploaded.</summary>
    public Color4 Color { get; set; } = Color4.White;

    public float Intensity { get; set; } = 1f;
}
