using GT67.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GT67.Editor;

/// <summary>A circle of lines that hugs the terrain surface to show where and how large the brush is.</summary>
public sealed class BrushRing
{
    private const int Segments = 72;

    private readonly Mesh _mesh;

    public BrushRing(Scene scene)
    {
        var vertices = new Vertex[Segments];
        var indices = new uint[Segments * 2];
        for (int i = 0; i < Segments; i++)
        {
            vertices[i] = new Vertex(Vector3.Zero, Vector3.UnitY, Vector2.Zero, new Vector4(1f, 0.9f, 0.2f, 1f));
            indices[i * 2] = (uint)i;
            indices[i * 2 + 1] = (uint)((i + 1) % Segments);
        }

        _mesh = new Mesh(vertices, indices, PrimitiveType.Lines);
        Object = new GameObject(_mesh, Vector3.Zero)
        {
            Name = "Brush",
            Unlit = true,
            CastShadow = false,
            ReceiveShadow = false,
            Collision = CollisionMode.None,
            EditorOnly = true,
            Visible = false,
        };
        scene.Add(Object);
    }

    public GameObject Object { get; }

    public void Hide() => Object.Visible = false;

    /// <summary>Places the ring around <paramref name="center"/>, lifting each point onto the terrain surface.</summary>
    public void Show(Terrain terrain, Vector3 center, float radius) => Show((x, z) => terrain.GetHeight(x, z), center, radius);

    /// <summary>Shows the ring following any ground: <paramref name="groundHeight"/> gives its height at (x, z).</summary>
    public void Show(Func<float, float, float> groundHeight, Vector3 center, float radius)
    {
        var vertices = _mesh.Vertices;
        for (int i = 0; i < Segments; i++)
        {
            float angle = MathF.Tau * i / Segments;
            float x = center.X + MathF.Cos(angle) * radius;
            float z = center.Z + MathF.Sin(angle) * radius;
            vertices[i].Position = new Vector3(x, groundHeight(x, z) + 0.08f, z);
        }
        _mesh.UpdateVertices();
        Object.Visible = true;
    }
}
