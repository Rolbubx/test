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

    /// <summary>Adds the indices of every triangle whose leaf box overlaps <paramref name="box"/>.</summary>
    public void Query(Aabb box, List<int> result)
    {
        if (_nodes.Count == 0) return;
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            var node = _nodes[stack.Pop()];
            if (node.Min.X > box.Max.X || node.Max.X < box.Min.X ||
                node.Min.Y > box.Max.Y || node.Max.Y < box.Min.Y ||
                node.Min.Z > box.Max.Z || node.Max.Z < box.Min.Z) continue;

            if (node.IsLeaf)
            {
                for (int i = 0; i < node.Right; i++) result.Add(_order[node.Left + i]);
            }
            else
            {
                stack.Push(node.Left);
                stack.Push(node.Right);
            }
        }
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
