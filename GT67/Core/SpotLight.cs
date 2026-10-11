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

    /// <summary>World position (an attached light gets it from its object every frame).</summary>
    public Vector3 Position { get; set; }

    /// <summary>Where the cone points, in the world (an attached light turns with its object).</summary>
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

    /// <summary>The direction in the followed object's own coordinates (set by <see cref="AttachTo"/>).</summary>
    public Vector3 FollowDirection { get; set; } = -Vector3.UnitY;

    /// <summary>Attaches the light to <paramref name="obj"/> as it is now (place and direction), so it moves and turns with it; null detaches it.</summary>
    public void AttachTo(GameObject? obj)
    {
        Follow = obj;
        if (obj == null) return;
        FollowOffset = ToLocal(obj, Position);
        FollowDirection = Vector3.Transform(Direction, obj.Transform.WorldRotation.Inverted());
    }

    internal override void FollowObject()
    {
        if (Follow is not { IsDestroyed: false } target)
        {
            Follow = null;
            return;
        }
        Position = ToWorld(target, FollowOffset);
        Direction = Vector3.Transform(FollowDirection, target.Transform.WorldRotation);
    }
}
