using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>Axis-aligned bounding box.</summary>
public readonly struct Aabb
{
    public Vector3 Min { get; }
    public Vector3 Max { get; }

    public Aabb(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }

    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Size => Max - Min;
    public Vector3 Extents => Size * 0.5f;

    /// <summary>An inverted box that becomes valid after the first Encapsulate call.</summary>
    public static Aabb Empty => new(new Vector3(float.MaxValue), new Vector3(float.MinValue));

    public bool IsValid => Min.X <= Max.X && Min.Y <= Max.Y && Min.Z <= Max.Z;

    public Aabb Encapsulate(Vector3 p) => new(Vector3.ComponentMin(Min, p), Vector3.ComponentMax(Max, p));

    public Aabb Encapsulate(Aabb other) =>
        new(Vector3.ComponentMin(Min, other.Min), Vector3.ComponentMax(Max, other.Max));

    /// <summary>Bounding box of this box after applying <paramref name="m"/>.</summary>
    public Aabb Transformed(Matrix4 m)
    {
        var result = Empty;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? Min.X : Max.X,
                (i & 2) == 0 ? Min.Y : Max.Y,
                (i & 4) == 0 ? Min.Z : Max.Z);
            result = result.Encapsulate(Vector3.TransformPosition(corner, m));
        }
        return result;
    }

    public bool Contains(Vector3 p) =>
        p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;

    public bool Intersects(Aabb o) =>
        Min.X <= o.Max.X && Max.X >= o.Min.X &&
        Min.Y <= o.Max.Y && Max.Y >= o.Min.Y &&
        Min.Z <= o.Max.Z && Max.Z >= o.Min.Z;

    /// <summary>Slab test. <paramref name="t"/> is the entry distance (0 when the origin is inside).</summary>
    public bool IntersectRay(Ray ray, out float t)
    {
        float tMin = 0f, tMax = float.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            float o = ray.Origin[i], d = ray.Direction[i];
            if (MathF.Abs(d) < 1e-8f)
            {
                if (o < Min[i] || o > Max[i]) { t = 0; return false; }
                continue;
            }
            float inv = 1f / d;
            float t1 = (Min[i] - o) * inv, t2 = (Max[i] - o) * inv;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);
            if (tMin > tMax) { t = 0; return false; }
        }
        t = tMin;
        return true;
    }
}
