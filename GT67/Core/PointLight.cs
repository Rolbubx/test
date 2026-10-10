using OpenTK.Mathematics;

namespace GT67.Core;

public sealed class PointLight : Light
{
    public PointLight(Vector3 position, Color4? color = null, float intensity = 1f, float range = 15f)
    {
        Name = "Point Light";
        Position = position;
        Color = color ?? Color4.White;
        Intensity = intensity;
        Range = range;
    }

    public Vector3 Position { get; set; }

    /// <summary>Distance at which the light fades to zero.</summary>
    public float Range { get; set; }
}
