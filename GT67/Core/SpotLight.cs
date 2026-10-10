using OpenTK.Mathematics;

namespace GT67.Core;

public sealed class SpotLight : Light
{
    private Vector3 _direction = -Vector3.UnitY;

    public SpotLight(Vector3 position, Vector3 direction)
    {
        Name = "Spot Light";
        Position = position;
        Direction = direction;
    }

    public Vector3 Position { get; set; }

    public Vector3 Direction
    {
        get => _direction;
        set => _direction = value.LengthSquared > 0 ? Vector3.Normalize(value) : -Vector3.UnitY;
    }

    /// <summary>Full-intensity cone half-angle, degrees.</summary>
    public float InnerAngle { get; set; } = 15f;

    /// <summary>Cone half-angle where the light reaches zero, degrees.</summary>
    public float OuterAngle { get; set; } = 25f;

    public float Range { get; set; } = 25f;
}
