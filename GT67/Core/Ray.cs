using OpenTK.Mathematics;

namespace GT67.Core;

public readonly struct Ray
{
    public Vector3 Origin { get; }
    public Vector3 Direction { get; }

    public Ray(Vector3 origin, Vector3 direction)
    {
        Origin = origin;
        Direction = direction.LengthSquared > 0 ? Vector3.Normalize(direction) : -Vector3.UnitZ;
    }

    public Vector3 GetPoint(float t) => Origin + Direction * t;
}
