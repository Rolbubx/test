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

    /// <summary>World position (an attached light gets it from its object every frame).</summary>
    public Vector3 Position { get; set; }

    /// <summary>Distance at which the light fades to zero.</summary>
    public float Range { get; set; }

    /// <summary>Attaches the light to <paramref name="obj"/> where it is now, so it moves with it; null detaches it.</summary>
    public void AttachTo(GameObject? obj)
    {
        Follow = obj;
        if (obj != null) FollowOffset = ToLocal(obj, Position);
    }

    internal override void FollowObject()
    {
        if (Follow is { IsDestroyed: false } target) Position = ToWorld(target, FollowOffset);
        else Follow = null;
    }
}
