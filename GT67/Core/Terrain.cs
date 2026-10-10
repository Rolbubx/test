using System.Runtime.InteropServices;
using GT67.Utils;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>
/// Editable heightfield terrain: a grid of heights drawn as a mesh that is updated in place while you sculpt
/// (<see cref="Sculpt"/>) or paint (<see cref="Paint"/>) with a round brush. The terrain is a normal
/// <see cref="GameObject"/> (<see cref="Object"/>) with a triangle-mesh collider, so objects can stand on it
/// and <c>MoveAndSlide</c> works. Add it with <c>scene.Add(terrain)</c>.
/// </summary>
public sealed class Terrain : IDisposable
{
    private readonly float[] _heights;
    private readonly int _stride;
    private bool _disposed;

    /// <param name="width">Size along X.</param>
    /// <param name="depth">Size along Z.</param>
    /// <param name="resolution">Grid cells per side (the mesh has (resolution + 1)^2 vertices).</param>
    /// <param name="baseHeight">Starting height of the flat ground.</param>
    public Terrain(float width, float depth, int resolution = 128, float baseHeight = 0f)
    {
        Width = width;
        Depth = depth;
        Resolution = Math.Clamp(resolution, 8, 512);
        _stride = Resolution + 1;
        _heights = new float[_stride * _stride];
        Array.Fill(_heights, baseHeight);

        var vertices = new Vertex[_stride * _stride];
        for (int z = 0; z < _stride; z++)
        for (int x = 0; x < _stride; x++)
        {
            vertices[z * _stride + x] = new Vertex(
                new Vector3(x * CellWidth - width * 0.5f, baseHeight, z * CellDepth - depth * 0.5f),
                Vector3.UnitY,
                new Vector2((float)x / Resolution, 1f - (float)z / Resolution),
                new Vector4(0.36f, 0.5f, 0.24f, 1f)); // grass green until painted
        }

        var indices = new uint[Resolution * Resolution * 6];
        int i = 0;
        for (int z = 0; z < Resolution; z++)
        for (int x = 0; x < Resolution; x++)
        {
            uint a = (uint)(z * _stride + x), b = a + (uint)_stride;
            indices[i++] = a; indices[i++] = b; indices[i++] = b + 1;
            indices[i++] = a; indices[i++] = b + 1; indices[i++] = a + 1;
        }

        Object = new GameObject(new Mesh(vertices, indices), 0, 0, 0)
        {
            Name = "Terrain",
            Tag = "terrain",
        };
        Object.Material.SpecularStrength = 0.04f;
        Object.Material.Shininess = 8f;
    }

    /// <summary>The renderable, collidable object. Move it up and down to offset the whole terrain (do not rotate or scale it).</summary>
    public GameObject Object { get; }

    public float Width { get; }
    public float Depth { get; }
    public int Resolution { get; }

    public float CellWidth => Width / Resolution;
    public float CellDepth => Depth / Resolution;

    private Mesh Mesh => Object.Model.Parts[0].Mesh;
    private Vertex[] Vertices => Mesh.Vertices;

    // ---- height queries --------------------------------------------------------------------------

    /// <summary>Raw height at a grid point (0..Resolution), before the object's own offset.</summary>
    public float GetGridHeight(int x, int z) => _heights[Math.Clamp(z, 0, Resolution) * _stride + Math.Clamp(x, 0, Resolution)];

    /// <summary>World-space height of the ground at (x, z) (bilinear; clamped to the terrain edge).</summary>
    public float GetHeight(float worldX, float worldZ)
    {
        float fx = (worldX - Object.Position.X + Width * 0.5f) / CellWidth;
        float fz = (worldZ - Object.Position.Z + Depth * 0.5f) / CellDepth;
        fx = Math.Clamp(fx, 0f, Resolution);
        fz = Math.Clamp(fz, 0f, Resolution);

        int x0 = Math.Min((int)fx, Resolution - 1), z0 = Math.Min((int)fz, Resolution - 1);
        float tx = fx - x0, tz = fz - z0;
        float h00 = _heights[z0 * _stride + x0], h10 = _heights[z0 * _stride + x0 + 1];
        float h01 = _heights[(z0 + 1) * _stride + x0], h11 = _heights[(z0 + 1) * _stride + x0 + 1];
        float h = (h00 * (1 - tx) + h10 * tx) * (1 - tz) + (h01 * (1 - tx) + h11 * tx) * tz;
        return h + Object.Position.Y;
    }

    /// <summary>Marches a ray down onto the surface. Returns the first point where the ray goes below the ground.</summary>
    public bool Raycast(Ray ray, out Vector3 hit, float maxDistance = 1000f)
    {
        hit = Vector3.Zero;
        float step = MathF.Min(CellWidth, CellDepth) * 0.5f;
        float previous = 0f;
        bool wasAbove = ray.Origin.Y > GetHeight(ray.Origin.X, ray.Origin.Z);

        for (float t = step; t <= maxDistance; t += step)
        {
            var p = ray.GetPoint(t);
            bool above = p.Y > GetHeight(p.X, p.Z);
            if (wasAbove && !above)
            {
                // Refine the crossing between the previous and current step.
                float lo = previous, hi = t;
                for (int i = 0; i < 12; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    var m = ray.GetPoint(mid);
                    if (m.Y > GetHeight(m.X, m.Z)) lo = mid; else hi = mid;
                }
                hit = ray.GetPoint(hi);
                hit.Y = GetHeight(hit.X, hit.Z);
                return true;
            }
            wasAbove = above;
            previous = t;
        }
        return false;
    }

    // ---- editing ---------------------------------------------------------------------------------

    /// <summary>
    /// Applies one frame of a brush stroke. <paramref name="strength"/> is roughly metres per second at the brush
    /// centre; <paramref name="flattenHeight"/> (world height) is the target for <see cref="TerrainBrush.Flatten"/>.
    /// Call <see cref="Apply"/> afterwards to push the change to the screen.
    /// </summary>
    public void Sculpt(TerrainBrush brush, Vector3 center, float radius, float strength, float deltaTime,
        float flattenHeight = 0f)
    {
        var copy = brush == TerrainBrush.Smooth ? (float[])_heights.Clone() : null;
        float flatten = flattenHeight - Object.Position.Y;

        ForEachInBrush(center, radius, (x, z, weight, wx, wz) =>
        {
            int i = z * _stride + x;
            switch (brush)
            {
                case TerrainBrush.Raise: _heights[i] += strength * weight * deltaTime; break;
                case TerrainBrush.Lower: _heights[i] -= strength * weight * deltaTime; break;
                case TerrainBrush.Flatten:
                    _heights[i] += (flatten - _heights[i]) * Math.Clamp(weight * strength * deltaTime, 0f, 1f);
                    break;
                case TerrainBrush.Noise:
                    _heights[i] += (Fbm(wx * 0.35f, wz * 0.35f) - 0.5f) * 2f * strength * weight * deltaTime;
                    break;
                case TerrainBrush.Smooth:
                    float sum = 0f;
                    int count = 0;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nz < 0 || nx > Resolution || nz > Resolution) continue;
                        sum += copy![nz * _stride + nx];
                        count++;
                    }
                    _heights[i] += (sum / count - _heights[i]) * Math.Clamp(weight * strength * deltaTime * 2f, 0f, 1f);
                    break;
            }
        });
    }

    /// <summary>
    /// Digs a smooth bowl for a lake: inside a rounded rectangle centred on <paramref name="center"/> (half sizes
    /// <paramref name="halfWidth"/> x <paramref name="halfDepth"/>), the ground slopes down to <paramref name="bottom"/>
    /// (world height); outside it nothing changes, and ground that is already lower stays as it is. Applies the change
    /// and rebuilds the collision.
    /// </summary>
    public void CarveBasin(Vector3 center, float halfWidth, float halfDepth, float bottom)
    {
        float radius = MathF.Min(halfWidth, halfDepth) * 0.6f;   // how round the shore is
        float bank = MathF.Min(halfWidth, halfDepth) * 0.55f;    // width of the slope down to the bottom
        float floor = bottom - Object.Position.Y;
        var origin = Object.Position - new Vector3(Width * 0.5f, 0f, Depth * 0.5f);

        for (int z = 0; z <= Resolution; z++)
        for (int x = 0; x <= Resolution; x++)
        {
            float wx = origin.X + x * CellWidth - center.X, wz = origin.Z + z * CellDepth - center.Z;
            // Signed distance to the rounded rectangle (negative inside).
            float qx = MathF.Abs(wx) - (halfWidth - radius), qz = MathF.Abs(wz) - (halfDepth - radius);
            float outside = new Vector2(MathF.Max(qx, 0f), MathF.Max(qz, 0f)).Length;
            float distance = outside + MathF.Min(MathF.Max(qx, qz), 0f) - radius;
            if (distance >= 0f) continue;

            int i = z * _stride + x;
            float t = Math.Clamp(-distance / bank, 0f, 1f);
            float target = _heights[i] + (floor - _heights[i]) * (t * t * (3f - 2f * t));
            _heights[i] = MathF.Min(_heights[i], target);
        }
        Apply();
        FinishEdit();
    }

    /// <summary>Blends the vertex colours under the brush towards <paramref name="color"/> (sRGB).</summary>
    public void Paint(Vector3 center, float radius, Color4 color, float strength, float deltaTime)
    {
        var target = new Vector4(color.R, color.G, color.B, 1f);
        ForEachInBrush(center, radius, (x, z, weight, _, _) =>
        {
            ref var vertex = ref Vertices[z * _stride + x];
            vertex.Color = Vector4.Lerp(vertex.Color, target, Math.Clamp(weight * strength * deltaTime, 0f, 1f));
        });
    }

    /// <summary>Refreshes normals and uploads the edited vertices (cheap enough to call every frame while brushing).</summary>
    public void Apply()
    {
        var vertices = Vertices;
        for (int z = 0; z < _stride; z++)
        for (int x = 0; x < _stride; x++)
        {
            int i = z * _stride + x;
            vertices[i].Position.Y = _heights[i];

            float left = _heights[z * _stride + Math.Max(x - 1, 0)], right = _heights[z * _stride + Math.Min(x + 1, Resolution)];
            float down = _heights[Math.Max(z - 1, 0) * _stride + x], up = _heights[Math.Min(z + 1, Resolution) * _stride + x];
            float dx = (Math.Min(x + 1, Resolution) - Math.Max(x - 1, 0)) * CellWidth;
            float dz = (Math.Min(z + 1, Resolution) - Math.Max(z - 1, 0)) * CellDepth;
            vertices[i].Normal = Vector3.Normalize(new Vector3(-(right - left) / dx, 1f, -(up - down) / dz));
        }
        Mesh.UpdateVertices();
        Object.Model.RecalculateBounds();
    }

    /// <summary>
    /// Rebuilds the collision data. Call when a stroke ends (it is slower than <see cref="Apply"/>); until then the old
    /// shape is used for collisions.
    /// </summary>
    public void FinishEdit() => Object.Model.InvalidateCollision();

    // ---- saving ----------------------------------------------------------------------------------

    /// <summary>Heights as base64 (float32, row by row).</summary>
    public string ExportHeights() => Convert.ToBase64String(MemoryMarshal.AsBytes<float>(_heights));

    /// <summary>Vertex colours as base64 (RGBA, one byte per channel).</summary>
    public string ExportColors()
    {
        var bytes = new byte[_stride * _stride * 4];
        for (int i = 0; i < _stride * _stride; i++)
        {
            var c = Vertices[i].Color;
            bytes[i * 4] = (byte)(Math.Clamp(c.X, 0f, 1f) * 255f + 0.5f);
            bytes[i * 4 + 1] = (byte)(Math.Clamp(c.Y, 0f, 1f) * 255f + 0.5f);
            bytes[i * 4 + 2] = (byte)(Math.Clamp(c.Z, 0f, 1f) * 255f + 0.5f);
            bytes[i * 4 + 3] = 255;
        }
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Loads heights (and optionally colours) written by <see cref="ExportHeights"/> / <see cref="ExportColors"/>.</summary>
    public void Import(string heightsBase64, string? colorsBase64 = null)
    {
        var heights = MemoryMarshal.Cast<byte, float>(Convert.FromBase64String(heightsBase64));
        if (heights.Length != _heights.Length)
            throw new ArgumentException($"Expected {_heights.Length} heights but got {heights.Length}.");
        heights.CopyTo(_heights);

        if (colorsBase64 != null)
        {
            var bytes = Convert.FromBase64String(colorsBase64);
            for (int i = 0; i < _stride * _stride && i * 4 + 2 < bytes.Length; i++)
                Vertices[i].Color = new Vector4(bytes[i * 4] / 255f, bytes[i * 4 + 1] / 255f, bytes[i * 4 + 2] / 255f, 1f);
        }
        Apply();
        FinishEdit();
    }

    // ---- helpers ---------------------------------------------------------------------------------

    /// <summary>Calls <paramref name="action"/> for each grid point inside the brush with a smooth 0..1 weight.</summary>
    private void ForEachInBrush(Vector3 center, float radius, Action<int, int, float, float, float> action)
    {
        float lx = center.X - Object.Position.X + Width * 0.5f;
        float lz = center.Z - Object.Position.Z + Depth * 0.5f;
        int minX = Math.Max(0, (int)MathF.Floor((lx - radius) / CellWidth));
        int maxX = Math.Min(Resolution, (int)MathF.Ceiling((lx + radius) / CellWidth));
        int minZ = Math.Max(0, (int)MathF.Floor((lz - radius) / CellDepth));
        int maxZ = Math.Min(Resolution, (int)MathF.Ceiling((lz + radius) / CellDepth));

        for (int z = minZ; z <= maxZ; z++)
        for (int x = minX; x <= maxX; x++)
        {
            float wx = x * CellWidth - lx, wz = z * CellDepth - lz;
            float d = MathF.Sqrt(wx * wx + wz * wz) / radius;
            if (d >= 1f) continue;
            float t = 1f - d;
            action(x, z, t * t * (3f - 2f * t), center.X + wx, center.Z + wz);
        }
    }

    /// <summary>Cheap smooth value noise in 0..1 (two octaves).</summary>
    private static float Fbm(float x, float z) => 0.65f * Value(x, z) + 0.35f * Value(x * 2.3f + 11f, z * 2.3f + 7f);

    private static float Value(float x, float z)
    {
        int ix = (int)MathF.Floor(x), iz = (int)MathF.Floor(z);
        float fx = x - ix, fz = z - iz;
        float sx = fx * fx * (3f - 2f * fx), sz = fz * fz * (3f - 2f * fz);
        float a = Hash(ix, iz), b = Hash(ix + 1, iz), c = Hash(ix, iz + 1), d = Hash(ix + 1, iz + 1);
        return (a + (b - a) * sx) + ((c + (d - c) * sx) - (a + (b - a) * sx)) * sz;
    }

    private static float Hash(int x, int z)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)z * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFu) / 65535f;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Object.Destroy();
    }
}
