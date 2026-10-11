using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>
/// Bounding-volume hierarchy over triangles in a model's local space. Built once per model (and again after the
/// mesh is edited); queried with a box to find the few triangles near a moving object.
/// </summary>
internal sealed class TriangleBvh
{
    private const int LeafSize = 6;

    private struct Node
    {
        public Vector3 Min, Max;
        public int Left;    // child index (inner) or first triangle slot (leaf)
        public int Right;   // child index (inner) or triangle count (leaf)
        public bool IsLeaf;
    }

    private readonly Vector3[] _vertices;   // 3 per triangle
    private readonly int[] _order;          // triangle indices, grouped by leaf
    private readonly List<Node> _nodes = new();

    public int TriangleCount { get; }

    private TriangleBvh(Vector3[] vertices)
    {
        _vertices = vertices;
        TriangleCount = vertices.Length / 3;
        _order = Enumerable.Range(0, TriangleCount).ToArray();
        if (TriangleCount > 0) Build(0, TriangleCount);
    }

    public static TriangleBvh FromModel(Model model)
    {
        var vertices = new List<Vector3>();
        foreach (var part in model.Parts)
        {
            var mesh = part.Mesh;
            if (mesh.PrimitiveType != OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles) continue;
            var v = mesh.Vertices;
            var idx = mesh.Indices;
            for (int i = 0; i + 2 < idx.Length; i += 3)
            {
                vertices.Add(v[idx[i]].Position);
                vertices.Add(v[idx[i + 1]].Position);
                vertices.Add(v[idx[i + 2]].Position);
            }
        }
        return new TriangleBvh(vertices.ToArray());
    }

    /// <summary>The 12 triangles of an axis-aligned box.</summary>
    public static TriangleBvh FromBox(Aabb box)
    {
        var min = box.Min;
        var max = box.Max;
        var c = new Vector3[8];
        for (int i = 0; i < 8; i++)
            c[i] = new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);

        int[][] faces =
        {
            new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 }, new[] { 0, 2, 6, 4 },
            new[] { 1, 5, 7, 3 }, new[] { 0, 4, 5, 1 }, new[] { 2, 3, 7, 6 },
        };
        var vertices = new List<Vector3>();
        foreach (var f in faces)
        {
            vertices.AddRange(new[] { c[f[0]], c[f[1]], c[f[2]], c[f[0]], c[f[2]], c[f[3]] });
        }
        return new TriangleBvh(vertices.ToArray());
    }

    public (Vector3 a, Vector3 b, Vector3 c) Triangle(int index) =>
        (_vertices[index * 3], _vertices[index * 3 + 1], _vertices[index * 3 + 2]);

    // The tree is at most a few dozen levels deep, and a walk keeps at most one pending node per level
    private const int MaxStack = 128;

    /// <summary>Adds the indices of every triangle whose leaf box overlaps <paramref name="box"/>.</summary>
    public void Query(Aabb box, List<int> result)
    {
        if (_nodes.Count == 0) return;
        Span<int> stack = stackalloc int[MaxStack];
        int count = 0;
        stack[count++] = 0;
        while (count > 0)
        {
            var node = _nodes[stack[--count]];
            if (node.Min.X > box.Max.X || node.Max.X < box.Min.X ||
                node.Min.Y > box.Max.Y || node.Max.Y < box.Min.Y ||
                node.Min.Z > box.Max.Z || node.Max.Z < box.Min.Z) continue;

            if (node.IsLeaf)
            {
                for (int i = 0; i < node.Right; i++) result.Add(_order[node.Left + i]);
            }
            else if (count + 2 <= MaxStack)
            {
                stack[count++] = node.Left;
                stack[count++] = node.Right;
            }
        }
    }

    /// <summary>
    /// The nearest triangle hit by a ray in the model's own space. <paramref name="direction"/> need not be unit length:
    /// <paramref name="distance"/> is measured in multiples of it. Only the boxes the ray crosses are visited, so a model
    /// of millions of triangles costs a few dozen box tests and a handful of triangle tests.
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 direction, out float distance)
    {
        distance = float.MaxValue;
        if (_nodes.Count == 0) return false;

        var inverse = new Vector3(
            MathF.Abs(direction.X) > 1e-12f ? 1f / direction.X : float.MaxValue,
            MathF.Abs(direction.Y) > 1e-12f ? 1f / direction.Y : float.MaxValue,
            MathF.Abs(direction.Z) > 1e-12f ? 1f / direction.Z : float.MaxValue);

        bool hit = false;
        Span<int> stack = stackalloc int[MaxStack];
        int count = 0;
        stack[count++] = 0;
        while (count > 0)
        {
            var node = _nodes[stack[--count]];
            if (!RayHitsBox(origin, inverse, node.Min, node.Max, distance)) continue;

            if (node.IsLeaf)
            {
                for (int i = 0; i < node.Right; i++)
                {
                    int t = _order[node.Left + i];
                    if (Utils.MathUtil.RayTriangle(origin, direction, _vertices[t * 3], _vertices[t * 3 + 1], _vertices[t * 3 + 2], out float d) &&
                        d < distance)
                    {
                        distance = d;
                        hit = true;
                    }
                }
            }
            else if (count + 2 <= MaxStack)
            {
                stack[count++] = node.Left;
                stack[count++] = node.Right;
            }
        }
        return hit;
    }

    /// <summary>Slab test: does the ray cross the box closer than <paramref name="maxDistance"/>?</summary>
    private static bool RayHitsBox(Vector3 origin, Vector3 inverse, Vector3 min, Vector3 max, float maxDistance)
    {
        float t1 = (min.X - origin.X) * inverse.X, t2 = (max.X - origin.X) * inverse.X;
        float near = MathF.Min(t1, t2), far = MathF.Max(t1, t2);
        t1 = (min.Y - origin.Y) * inverse.Y; t2 = (max.Y - origin.Y) * inverse.Y;
        near = MathF.Max(near, MathF.Min(t1, t2)); far = MathF.Min(far, MathF.Max(t1, t2));
        t1 = (min.Z - origin.Z) * inverse.Z; t2 = (max.Z - origin.Z) * inverse.Z;
        near = MathF.Max(near, MathF.Min(t1, t2)); far = MathF.Min(far, MathF.Max(t1, t2));
        return far >= MathF.Max(near, 0f) && near < maxDistance;
    }

    private int Build(int start, int count)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var centroidMin = new Vector3(float.MaxValue);
        var centroidMax = new Vector3(float.MinValue);
        for (int i = start; i < start + count; i++)
        {
            int t = _order[i];
            var a = _vertices[t * 3];
            var b = _vertices[t * 3 + 1];
            var c = _vertices[t * 3 + 2];
            min = Vector3.ComponentMin(min, Vector3.ComponentMin(a, Vector3.ComponentMin(b, c)));
            max = Vector3.ComponentMax(max, Vector3.ComponentMax(a, Vector3.ComponentMax(b, c)));
            var centroid = (a + b + c) / 3f;
            centroidMin = Vector3.ComponentMin(centroidMin, centroid);
            centroidMax = Vector3.ComponentMax(centroidMax, centroid);
        }

        int nodeIndex = _nodes.Count;
        _nodes.Add(new Node { Min = min, Max = max });

        var extent = centroidMax - centroidMin;
        if (count <= LeafSize || extent.LengthSquared < 1e-12f)
        {
            _nodes[nodeIndex] = new Node { Min = min, Max = max, Left = start, Right = count, IsLeaf = true };
            return nodeIndex;
        }

        // Split at the median centroid along the longest axis.
        int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
        var keys = new float[count];
        var slice = new int[count];
        for (int i = 0; i < count; i++)
        {
            int t = _order[start + i];
            keys[i] = ((_vertices[t * 3] + _vertices[t * 3 + 1] + _vertices[t * 3 + 2]) / 3f)[axis];
            slice[i] = t;
        }
        Array.Sort(keys, slice);
        Array.Copy(slice, 0, _order, start, count);

        int half = count / 2;
        int left = Build(start, half);
        int right = Build(start + half, count - half);
        _nodes[nodeIndex] = new Node { Min = min, Max = max, Left = left, Right = right, IsLeaf = false };
        return nodeIndex;
    }
}
