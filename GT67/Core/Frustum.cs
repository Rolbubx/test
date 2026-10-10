using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>
/// The six planes of a view volume, taken from a view-projection matrix (OpenTK's row-vector convention:
/// <c>clip = position * (view * projection)</c>). Works for perspective cameras, mirrored views and the sun's
/// orthographic shadow box alike.
/// </summary>
public readonly struct Frustum
{
    private readonly Vector4 _left, _right, _bottom, _top, _near, _far;

    public Frustum(Matrix4 viewProjection)
    {
        var m = viewProjection;
        var c0 = m.Column0;
        var c1 = m.Column1;
        var c2 = m.Column2;
        var c3 = m.Column3;
        _left = c3 + c0;
        _right = c3 - c0;
        _bottom = c3 + c1;
        _top = c3 - c1;
        _near = c3 + c2;   // OpenGL clip space: -w <= z <= w
        _far = c3 - c2;
    }

    /// <summary>False only when the box lies entirely outside one of the planes (so it certainly cannot be seen).</summary>
    public bool Intersects(Aabb box) =>
        Inside(_left, box) && Inside(_right, box) && Inside(_bottom, box) &&
        Inside(_top, box) && Inside(_near, box) && Inside(_far, box);

    private static bool Inside(Vector4 plane, Aabb box)
    {
        // The box corner furthest along the plane normal: if even that one is behind the plane, all of them are.
        float x = plane.X >= 0f ? box.Max.X : box.Min.X;
        float y = plane.Y >= 0f ? box.Max.Y : box.Min.Y;
        float z = plane.Z >= 0f ? box.Max.Z : box.Min.Z;
        return plane.X * x + plane.Y * y + plane.Z * z + plane.W >= 0f;
    }
}
