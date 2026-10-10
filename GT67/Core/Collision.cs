using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>Sphere-versus-world collision: pushes a sphere out of solid objects (box, sphere and triangle-mesh colliders).</summary>
internal static class Collision
{
    private const int Iterations = 4;

    /// <summary>
    /// Moves a sphere by <paramref name="delta"/> and slides it along whatever it hits. Large moves are split into
    /// steps no longer than half the radius so thin walls are not skipped.
    /// </summary>
    public static MoveResult MoveSphere(IReadOnlyList<GameObject> objects, Vector3 start, Vector3 delta, float radius,
        GameObject? ignore, Action<GameObject, Vector3>? onContact)
    {
        int steps = Math.Max(1, (int)MathF.Ceiling(delta.Length / MathF.Max(radius * 0.5f, 0.01f)));
        var step = delta / steps;
        var position = start;
        var groundNormal = Vector3.UnitY;
        GameObject? ground = null;
        bool collided = false, grounded = false;
        var triangles = new List<int>();

        for (int s = 0; s < steps; s++)
        {
            position += step;
            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                bool pushed = false;
                foreach (var obj in objects)
                {
                    if (obj == ignore || obj.Collision == CollisionMode.None || !obj.Enabled || obj.IsDestroyed) continue;
                    if (Push(obj, ref position, radius, triangles, out var normal))
                    {
                        pushed = true;
                        collided = true;
                        onContact?.Invoke(obj, normal);
                        if (normal.Y > 0.5f && (!grounded || normal.Y > groundNormal.Y))
                        {
                            grounded = true;
                            groundNormal = normal;
                            ground = obj;
                        }
                    }
                }
                if (!pushed) break;
            }
        }
        return new MoveResult(position, collided, grounded, groundNormal, ground);
    }

    /// <summary>Pushes the sphere out of one object. Returns true (and the average push direction) if it overlapped.</summary>
    private static bool Push(GameObject obj, ref Vector3 position, float radius, List<int> triangles, out Vector3 normal)
    {
        normal = Vector3.Zero;
        var sphereBox = new Aabb(position - new Vector3(radius), position + new Vector3(radius));
        if (!obj.Bounds.Intersects(sphereBox)) return false;

        if (obj.Collision == CollisionMode.Sphere)
        {
            var bound = obj.BoundingSphere;
            var offset = position - bound.Center;
            float distance = offset.Length;
            float reach = bound.Radius + radius;
            if (distance >= reach) return false;

            normal = distance > 1e-5f ? offset / distance : Vector3.UnitY;
            position += normal * (reach - distance);
            return true;
        }

        var bvh = obj.Collision == CollisionMode.Box ? obj.Model.GetBoxBvh() : obj.Model.GetMeshBvh();
        if (bvh.TriangleCount == 0) return false;

        var world = obj.Transform.WorldMatrix;
        if (MathF.Abs(world.Determinant) < 1e-12f) return false;
        var localBox = sphereBox.Transformed(Matrix4.Invert(world));

        triangles.Clear();
        bvh.Query(localBox, triangles);

        bool pushedAny = false;
        var sum = Vector3.Zero;
        foreach (int t in triangles)
        {
            var (la, lb, lc) = bvh.Triangle(t);
            var a = Vector3.TransformPosition(la, world);
            var b = Vector3.TransformPosition(lb, world);
            var c = Vector3.TransformPosition(lc, world);

            var closest = ClosestPointOnTriangle(position, a, b, c);
            if (float.IsNaN(closest.X + closest.Y + closest.Z)) continue;
            var offset = position - closest;
            float distance = offset.Length;
            if (distance >= radius) continue;

            Vector3 direction;
            if (distance > 1e-5f)
            {
                direction = offset / distance;
            }
            else
            {
                direction = Vector3.Cross(b - a, c - a);
                direction = direction.LengthSquared > 1e-12f ? Vector3.Normalize(direction) : Vector3.UnitY;
            }

            position += direction * (radius - distance);
            sum += direction;
            pushedAny = true;
        }

        if (pushedAny) normal = sum.LengthSquared > 1e-8f ? Vector3.Normalize(sum) : Vector3.UnitY;
        return pushedAny;
    }

    /// <summary>Closest point to <paramref name="p"/> on triangle abc (Ericson, Real-Time Collision Detection).</summary>
    public static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;

        var bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));

        var cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

        float sum = va + vb + vc;
        if (MathF.Abs(sum) < 1e-20f) return a; // zero-area triangle (e.g. at the pole of a UV sphere)
        float denom = 1f / sum;
        return a + ab * (vb * denom) + ac * (vc * denom);
    }
}
