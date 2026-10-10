using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>
/// A field of grass: thousands of blades, each a small curved strip rooted at a point on the ground, drawn with GPU
/// instancing (one draw call per 16 m chunk on screen). Every blade has its own height, width, facing, lean and shade;
/// they sway in gusty wind, bend away from the player walking through them, catch the sun's shadows, glow when the sun
/// is behind them, and thin out with distance. Paint blades in the editor (Terrain tool, Grass brush) or from code
/// with <see cref="Scatter"/> / <see cref="Paint"/>; set <c>scene.Grass</c> to show them.
/// </summary>
public sealed class GrassLayer : IDisposable
{
    private const float ChunkSize = 16f;
    private const int Segments = 4;

    private sealed class Chunk
    {
        public readonly List<Vector4> Blades = new();   // root x, y, z, seed 0..1
        public int Vao, InstanceBuffer, Uploaded = -1;
        public bool Dirty = true;
        public float MinY = float.MaxValue, MaxY = float.MinValue;
    }

    private readonly Dictionary<(int x, int z), Chunk> _chunks = new();
    private int _bladeBuffer, _indexBuffer, _indexCount;
    private bool _disposed;

    public bool Enabled { get; set; } = true;

    /// <summary>Typical blade height in metres (each blade varies around it).</summary>
    public float Height { get; set; } = 0.45f;

    /// <summary>Blade width at the root, in metres.</summary>
    public float Width { get; set; } = 0.07f;

    /// <summary>Colour at the root and at the tip (sRGB).</summary>
    public Color4 BaseColor { get; set; } = new(0.13f, 0.28f, 0.06f, 1f);
    public Color4 TipColor { get; set; } = new(0.55f, 0.72f, 0.24f, 1f);

    /// <summary>How far the wind bends the blades (0 = still), how fast it gusts, and where it blows (x, z).</summary>
    public float WindStrength { get; set; } = 0.35f;
    public float WindSpeed { get; set; } = 1.6f;
    public Vector2 WindDirection { get; set; } = new(1f, 0.3f);

    /// <summary>Beyond this distance no grass is drawn; it thins out gradually before it.</summary>
    public float MaxDistance { get; set; } = 70f;

    /// <summary>Share of blades that are wildflowers (white, yellow, purple), 0..1 (0 = none).</summary>
    public float Flowers { get; set; } = 0.015f;

    /// <summary>Bend away from objects tagged "Player" when they walk through (radius in metres, 0 = off).</summary>
    public float PlayerBendRadius { get; set; } = 0.8f;

    /// <summary>Number of blades.</summary>
    public int Count => _chunks.Values.Sum(c => c.Blades.Count);

    // ---- adding and removing blades ----------------------------------------------------------------

    /// <summary>Adds one blade rooted at <paramref name="root"/>; <paramref name="seed"/> (0..1) picks its look.</summary>
    public void Add(Vector3 root, float seed)
    {
        var chunk = ChunkAt(root.X, root.Z, create: true)!;
        chunk.Blades.Add(new Vector4(root.X, root.Y, root.Z, seed));
        chunk.MinY = MathF.Min(chunk.MinY, root.Y);
        chunk.MaxY = MathF.Max(chunk.MaxY, root.Y);
        chunk.Dirty = true;
    }

    /// <summary>
    /// Fills the rectangle <paramref name="min"/>..<paramref name="max"/> (x, z) with <paramref name="density"/> blades
    /// per square metre. <paramref name="groundHeight"/> gives the ground height at (x, z), or null where no grass should
    /// grow (water, rock). Returns how many blades were added.
    /// </summary>
    public int Scatter(Vector2 min, Vector2 max, float density, Func<float, float, float?> groundHeight, int seed = 1)
    {
        var random = new Random(seed);
        int count = (int)((max.X - min.X) * (max.Y - min.Y) * MathF.Max(density, 0f));
        int added = 0;
        for (int i = 0; i < count; i++)
        {
            float x = min.X + (float)random.NextDouble() * (max.X - min.X);
            float z = min.Y + (float)random.NextDouble() * (max.Y - min.Y);
            if (groundHeight(x, z) is not { } y) continue;
            Add(new Vector3(x, y, z), (float)random.NextDouble());
            added++;
        }
        return added;
    }

    /// <summary>
    /// Brush: adds blades in the circle until it holds <paramref name="density"/> blades per square metre (so going over
    /// the same place again does not pile them up). Returns how many were added.
    /// </summary>
    public int Paint(Vector2 center, float radius, float density, Func<float, float, float?> groundHeight, Random random, int maxPerCall = 400)
    {
        float area = MathF.PI * radius * radius;
        int missing = (int)(area * density) - CountIn(center, radius);
        int added = 0;
        for (int i = 0; i < Math.Min(missing, maxPerCall) * 2 && added < Math.Min(missing, maxPerCall); i++)
        {
            float angle = (float)random.NextDouble() * MathF.Tau, r = radius * MathF.Sqrt((float)random.NextDouble());
            float x = center.X + MathF.Cos(angle) * r, z = center.Y + MathF.Sin(angle) * r;
            if (groundHeight(x, z) is not { } y) continue;
            Add(new Vector3(x, y, z), (float)random.NextDouble());
            added++;
        }
        return added;
    }

    /// <summary>Brush: removes about <paramref name="fraction"/> (0..1) of the blades in the circle. Returns how many.</summary>
    public int Erase(Vector2 center, float radius, float fraction, Random random)
    {
        int removed = 0;
        foreach (var chunk in ChunksNear(center, radius))
        {
            removed += chunk.Blades.RemoveAll(b =>
                (new Vector2(b.X, b.Z) - center).LengthSquared <= radius * radius && random.NextDouble() < fraction);
            chunk.Dirty = true;
        }
        return removed;
    }

    /// <summary>Puts the blades in the circle back on the ground (after the terrain under them was sculpted).</summary>
    public void Reground(Vector2 center, float radius, Func<float, float, float?> groundHeight)
    {
        foreach (var chunk in ChunksNear(center, radius))
        {
            var blades = chunk.Blades;
            for (int i = 0; i < blades.Count; i++)
            {
                var b = blades[i];
                if ((new Vector2(b.X, b.Z) - center).LengthSquared > radius * radius) continue;
                if (groundHeight(b.X, b.Z) is { } y) blades[i] = b with { Y = y };
            }
            chunk.MinY = blades.Count > 0 ? blades.Min(b => b.Y) : float.MaxValue;
            chunk.MaxY = blades.Count > 0 ? blades.Max(b => b.Y) : float.MinValue;
            chunk.Dirty = true;
        }
    }

    /// <summary>Removes every blade.</summary>
    public void Clear()
    {
        foreach (var chunk in _chunks.Values) DeleteGl(chunk);
        _chunks.Clear();
    }

    /// <summary>Number of blades within <paramref name="radius"/> of <paramref name="center"/> (x, z).</summary>
    public int CountIn(Vector2 center, float radius)
    {
        int count = 0;
        foreach (var chunk in ChunksNear(center, radius))
            foreach (var b in chunk.Blades)
                if ((new Vector2(b.X, b.Z) - center).LengthSquared <= radius * radius) count++;
        return count;
    }

    // ---- saving --------------------------------------------------------------------------------------

    /// <summary>Every blade as base64 (four floats each), for the generated map.</summary>
    public string Export() =>
        Convert.ToBase64String(MemoryMarshal.AsBytes<Vector4>(_chunks.Values.SelectMany(c => c.Blades).ToArray()));

    /// <summary>Adds the blades of a string made by <see cref="Export"/>.</summary>
    public void Import(string base64)
    {
        foreach (var blade in MemoryMarshal.Cast<byte, Vector4>(Convert.FromBase64String(base64)).ToArray())
            Add(blade.Xyz, blade.W);
    }

    // ---- drawing ------------------------------------------------------------------------------------

    /// <summary>
    /// Draws the chunks that are in view and within <see cref="MaxDistance"/>; the shader (grass.vert/.frag) must be
    /// bound with its uniforms set. Returns the number of draw calls.
    /// </summary>
    internal int Draw(Frustum frustum, Vector3 eye)
    {
        if (!Enabled || _chunks.Count == 0) return 0;
        EnsureBladeMesh();
        int draws = 0;
        float reach = MaxDistance + ChunkSize;
        foreach (var ((cx, cz), chunk) in _chunks)
        {
            if (chunk.Blades.Count == 0) continue;
            var min = new Vector3(cx * ChunkSize, chunk.MinY - 0.1f, cz * ChunkSize);
            var max = new Vector3((cx + 1) * ChunkSize, chunk.MaxY + Height * 1.6f, (cz + 1) * ChunkSize);
            var nearest = Vector3.Clamp(eye, min, max);
            if ((nearest - eye).LengthSquared > reach * reach) continue;
            if (!frustum.Intersects(new Aabb(min, max))) continue;

            Upload(chunk);
            GL.BindVertexArray(chunk.Vao);
            GL.DrawElementsInstanced(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, IntPtr.Zero, chunk.Blades.Count);
            draws++;
        }
        GL.BindVertexArray(0);
        return draws;
    }

    /// <summary>One blade: a strip from y = 0 to 1 in <see cref="Segments"/> pieces, x from -0.5 to 0.5 (the shader shapes it).</summary>
    private void EnsureBladeMesh()
    {
        if (_bladeBuffer != 0) return;
        var vertices = new List<float>();
        for (int s = 0; s <= Segments; s++)
        {
            float t = (float)s / Segments;
            vertices.AddRange(new[] { -0.5f, t, 0f, 0.5f, t, 0f });
        }
        var indices = new List<uint>();
        for (uint s = 0; s < Segments; s++)
        {
            uint a = s * 2;
            indices.AddRange(new[] { a, a + 1, a + 3, a, a + 3, a + 2 });
        }
        _indexCount = indices.Count;

        _bladeBuffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _bladeBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.StaticDraw);
        _indexBuffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Count * sizeof(uint), indices.ToArray(), BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
    }

    private void Upload(Chunk chunk)
    {
        if (!chunk.Dirty && chunk.Vao != 0) return;
        if (chunk.Vao == 0)
        {
            chunk.Vao = GL.GenVertexArray();
            chunk.InstanceBuffer = GL.GenBuffer();
            GL.BindVertexArray(chunk.Vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _bladeBuffer);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
            GL.BindBuffer(BufferTarget.ArrayBuffer, chunk.InstanceBuffer);
            GL.EnableVertexAttribArray(5);
            GL.VertexAttribPointer(5, 4, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.VertexAttribDivisor(5, 1);
            GL.BindVertexArray(0);
        }
        var data = chunk.Blades.ToArray();
        GL.BindBuffer(BufferTarget.ArrayBuffer, chunk.InstanceBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, data.Length * 4 * sizeof(float), data, BufferUsageHint.DynamicDraw);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        chunk.Dirty = false;
    }

    // ---- chunks ------------------------------------------------------------------------------------

    private Chunk? ChunkAt(float x, float z, bool create)
    {
        var key = ((int)MathF.Floor(x / ChunkSize), (int)MathF.Floor(z / ChunkSize));
        if (_chunks.TryGetValue(key, out var chunk)) return chunk;
        if (!create) return null;
        return _chunks[key] = new Chunk();
    }

    private IEnumerable<Chunk> ChunksNear(Vector2 center, float radius)
    {
        int x0 = (int)MathF.Floor((center.X - radius) / ChunkSize), x1 = (int)MathF.Floor((center.X + radius) / ChunkSize);
        int z0 = (int)MathF.Floor((center.Y - radius) / ChunkSize), z1 = (int)MathF.Floor((center.Y + radius) / ChunkSize);
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
            if (_chunks.TryGetValue((x, z), out var chunk)) yield return chunk;
    }

    private static void DeleteGl(Chunk chunk)
    {
        if (chunk.Vao != 0) GL.DeleteVertexArray(chunk.Vao);
        if (chunk.InstanceBuffer != 0) GL.DeleteBuffer(chunk.InstanceBuffer);
        chunk.Vao = chunk.InstanceBuffer = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
        if (_bladeBuffer != 0) GL.DeleteBuffer(_bladeBuffer);
        if (_indexBuffer != 0) GL.DeleteBuffer(_indexBuffer);
    }
}
