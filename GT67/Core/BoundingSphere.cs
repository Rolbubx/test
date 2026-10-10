using OpenTK.Mathematics;

namespace GT67.Core;

public readonly struct BoundingSphere
{
    public Vector3 Center { get; }
    public float Radius { get; }

    public BoundingSphere(Vector3 center, float radius)
    {
        Center = center;
        Radius = radius;
    }

    public bool Intersects(BoundingSphere other)
    {
        float r = Radius + other.Radius;
        return (Center - other.Center).LengthSquared <= r * r;
    }

    public bool Contains(Vector3 p) => (p - Center).LengthSquared <= Radius * Radius;
}
