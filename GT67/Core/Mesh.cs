using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>GPU mesh (VAO/VBO/EBO). A CPU copy of the data is kept for picking.</summary>
public sealed class Mesh : IDisposable
{
    private int _vao, _vbo, _ebo;
    private bool _disposed;

    public Vertex[] Vertices { get; }
    public uint[] Indices { get; }
    public PrimitiveType PrimitiveType { get; }
    public Aabb Bounds { get; private set; }

    /// <summary>Where this mesh came from, as C# code that recreates it (e.g. "Primitives.Cube(1f)"); null if unknown.</summary>
    public string? Origin { get; set; }

    public Mesh(Vertex[] vertices, uint[] indices, PrimitiveType primitiveType = PrimitiveType.Triangles,
        bool generateTangents = false)
    {
        Vertices = vertices;
        Indices = indices;
        PrimitiveType = primitiveType;

        if (generateTangents && primitiveType == PrimitiveType.Triangles)
            GenerateTangents(vertices, indices);

        RecalculateBounds();

        Upload();
    }

    private void Upload()
    {
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        _ebo = GL.GenBuffer();

        GL.BindVertexArray(_vao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, Vertices.Length * Vertex.SizeInBytes, Vertices,
            BufferUsageHint.StaticDraw);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, Indices.Length * sizeof(uint), Indices,
            BufferUsageHint.StaticDraw);

        int stride = Vertex.SizeInBytes;
        Attribute(0, 3, nameof(Vertex.Position), stride);
        Attribute(1, 3, nameof(Vertex.Normal), stride);
        Attribute(2, 2, nameof(Vertex.UV), stride);
        Attribute(3, 3, nameof(Vertex.Tangent), stride);
        Attribute(4, 4, nameof(Vertex.Color), stride);

        GL.BindVertexArray(0);
    }

    private static void Attribute(int location, int components, string field, int stride)
    {
        int offset = (int)Marshal.OffsetOf<Vertex>(field);
        GL.EnableVertexAttribArray(location);
        GL.VertexAttribPointer(location, components, VertexAttribPointerType.Float, false, stride, offset);
    }

    /// <summary>Recomputes <see cref="Bounds"/> from the current vertex positions.</summary>
    public void RecalculateBounds()
    {
        var bounds = Aabb.Empty;
        foreach (var v in Vertices) bounds = bounds.Encapsulate(v.Position);
        Bounds = bounds.IsValid ? bounds : new Aabb(Vector3.Zero, Vector3.Zero);
    }

    /// <summary>
    /// Uploads the (edited in place) <see cref="Vertices"/> to the GPU and refreshes the bounds. Indices cannot change.
    /// Used by the terrain editor; collision data must be invalidated separately (<see cref="Model.InvalidateCollision"/>).
    /// </summary>
    public void UpdateVertices()
    {
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, Vertices.Length * Vertex.SizeInBytes, Vertices);
        RecalculateBounds();
    }

    public void Draw()
    {
        GL.BindVertexArray(_vao);
        GL.DrawElements(PrimitiveType, Indices.Length, DrawElementsType.UnsignedInt, 0);
        GL.BindVertexArray(0);
    }

    /// <summary>Per-triangle normal accumulation, producing smooth normals.</summary>
    public static void GenerateNormals(Vertex[] vertices, uint[] indices)
    {
        for (int i = 0; i < vertices.Length; i++) vertices[i].Normal = Vector3.Zero;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            ref var a = ref vertices[indices[i]];
            ref var b = ref vertices[indices[i + 1]];
            ref var c = ref vertices[indices[i + 2]];
            var n = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            a.Normal += n;
            b.Normal += n;
            c.Normal += n;
        }

        for (int i = 0; i < vertices.Length; i++)
            vertices[i].Normal = vertices[i].Normal.LengthSquared > 0
                ? Vector3.Normalize(vertices[i].Normal)
                : Vector3.UnitY;
    }

    /// <summary>Per-vertex tangents from UV derivatives, orthogonalised against the normal.</summary>
    public static void GenerateTangents(Vertex[] vertices, uint[] indices)
    {
        var tangents = new Vector3[vertices.Length];

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            uint i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
            var e1 = vertices[i1].Position - vertices[i0].Position;
            var e2 = vertices[i2].Position - vertices[i0].Position;
            var d1 = vertices[i1].UV - vertices[i0].UV;
            var d2 = vertices[i2].UV - vertices[i0].UV;

            float det = d1.X * d2.Y - d2.X * d1.Y;
            if (MathF.Abs(det) < 1e-12f) continue;

            var t = (e1 * d2.Y - e2 * d1.Y) / det;
            tangents[i0] += t;
            tangents[i1] += t;
            tangents[i2] += t;
        }

        for (int i = 0; i < vertices.Length; i++)
        {
            var n = vertices[i].Normal;
            var t = tangents[i] - n * Vector3.Dot(n, tangents[i]);
            if (t.LengthSquared < 1e-12f)
            {
                // Pick any vector perpendicular to the normal.
                var helper = MathF.Abs(n.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
                t = Vector3.Cross(helper, n);
            }
            vertices[i].Tangent = Vector3.Normalize(t);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GL.DeleteVertexArray(_vao);
        GL.DeleteBuffer(_vbo);
        GL.DeleteBuffer(_ebo);
    }
}
