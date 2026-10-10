using OpenTK.Mathematics;

namespace GT67.Utils;

public static class MathUtil
{
    public const float Deg2Rad = MathF.PI / 180f;
    public const float Rad2Deg = 180f / MathF.PI;

    /// <summary>Converts an sRGB colour to linear RGB (used for lighting math).</summary>
    public static Vector3 ToLinear(Color4 c) =>
        new(MathF.Pow(c.R, 2.2f), MathF.Pow(c.G, 2.2f), MathF.Pow(c.B, 2.2f));

    /// <summary>Converts linear RGB (clamped at 0) to an sRGB colour.</summary>
    public static Color4 ToSrgb(Vector3 linear) =>
        new(MathF.Pow(MathF.Max(linear.X, 0f), 1f / 2.2f),
            MathF.Pow(MathF.Max(linear.Y, 0f), 1f / 2.2f),
            MathF.Pow(MathF.Max(linear.Z, 0f), 1f / 2.2f), 1f);

    public static Quaternion FromEulerDegrees(Vector3 degrees) =>
        Quaternion.FromEulerAngles(degrees * Deg2Rad);

    public static Vector3 ToEulerDegrees(Quaternion q) =>
        q.ToEulerAngles() * Rad2Deg;

    /// <summary>Rotation whose local -Z axis points along <paramref name="forward"/>.</summary>
    public static Quaternion LookRotation(Vector3 forward, Vector3 up)
    {
        if (forward.LengthSquared < 1e-10f) return Quaternion.Identity;
        forward.Normalize();
        // Avoid a degenerate basis when looking straight along the up vector.
        if (MathF.Abs(Vector3.Dot(forward, up)) > 0.999f)
            up = MathF.Abs(forward.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;

        var right = Vector3.Normalize(Vector3.Cross(forward, up));
        var realUp = Vector3.Cross(right, forward);
        var back = -forward;
        var m = new Matrix3(right, realUp, back);
        return Quaternion.FromMatrix(Matrix3.Transpose(m)).Normalized(); // FromMatrix expects the transposed layout
    }

    public static Vector3 Rotate(Vector3 v, Quaternion q) => Vector3.Transform(v, q);

    public static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;

    /// <summary>Möller–Trumbore ray/triangle test. Returns the distance along the ray.</summary>
    public static bool RayTriangle(Vector3 origin, Vector3 dir, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        t = 0;
        var e1 = b - a;
        var e2 = c - a;
        var p = Vector3.Cross(dir, e2);
        float det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-8f) return false;

        float inv = 1f / det;
        var s = origin - a;
        float u = Vector3.Dot(s, p) * inv;
        if (u < 0f || u > 1f) return false;

        var q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(dir, q) * inv;
        if (v < 0f || u + v > 1f) return false;

        t = Vector3.Dot(e2, q) * inv;
        return t > 1e-5f;
    }
}
