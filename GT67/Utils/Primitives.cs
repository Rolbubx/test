using GT67.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GT67.Utils;

/// <summary>Procedural meshes. Every triangle mesh has normals, UVs and tangents.</summary>
public static class Primitives
{
    /// <summary>Remembers the call that built the mesh, so the map generator can write it back as code.</summary>
    private static Mesh Tag(Mesh mesh, string origin)
    {
        mesh.Origin = origin;
        return mesh;
    }

    private static string F(float value) =>
        value.ToString("0.#########", System.Globalization.CultureInfo.InvariantCulture) + "f";

    /// <summary>Cube centred on the origin, <paramref name="size"/> units per side.</summary>
    public static Mesh Cube(float size = 1f)
    {
        float h = size * 0.5f;
        // (normal, u axis, v axis) with u x v == normal so the winding is counter-clockwise from outside.
        var faces = new (Vector3 n, Vector3 u, Vector3 v)[]
        {
            (Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY),
            (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
            (Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ),
            (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
            (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),
            (-Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY),
        };

        var vertices = new List<Vertex>(24);
        var indices = new List<uint>(36);
        foreach (var (n, u, v) in faces)
        {
            uint start = (uint)vertices.Count;
            vertices.Add(new Vertex((n - u - v) * h, n, new Vector2(0, 0)));
            vertices.Add(new Vertex((n + u - v) * h, n, new Vector2(1, 0)));
            vertices.Add(new Vertex((n + u + v) * h, n, new Vector2(1, 1)));
            vertices.Add(new Vertex((n - u + v) * h, n, new Vector2(0, 1)));
            indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
        return Tag(new Mesh(vertices.ToArray(), indices.ToArray(), generateTangents: true), $"Primitives.Cube({F(size)})");
    }

    /// <summary>Flat plane in the XZ plane facing +Y. UVs span 0..1 over the whole plane.</summary>
    public static Mesh Plane(float width = 1f, float depth = 1f, int subdivisions = 1)
    {
        int n = Math.Max(1, subdivisions);
        var vertices = new Vertex[(n + 1) * (n + 1)];
        for (int z = 0; z <= n; z++)
        for (int x = 0; x <= n; x++)
        {
            float fx = (float)x / n, fz = (float)z / n;
            vertices[z * (n + 1) + x] = new Vertex(
                new Vector3((fx - 0.5f) * width, 0, (fz - 0.5f) * depth),
                Vector3.UnitY,
                new Vector2(fx, 1f - fz));
        }

        var indices = new List<uint>(n * n * 6);
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            uint a = (uint)(z * (n + 1) + x);
            uint b = a + (uint)(n + 1); // next row (+Z)
            indices.AddRange(new[] { a, b, b + 1, a, b + 1, a + 1 });
        }
        return Tag(new Mesh(vertices, indices.ToArray(), generateTangents: true), $"Primitives.Plane({F(width)}, {F(depth)}, {subdivisions})");
    }

    /// <summary>UV sphere centred on the origin.</summary>
    public static Mesh Sphere(float radius = 0.5f, int segments = 32)
    {
        int lon = Math.Max(3, segments);
        int lat = Math.Max(2, segments / 2);

        var vertices = new Vertex[(lat + 1) * (lon + 1)];
        for (int y = 0; y <= lat; y++)
        for (int x = 0; x <= lon; x++)
        {
            float theta = MathF.PI * y / lat;
            float phi = MathF.Tau * x / lon;
            var normal = new Vector3(MathF.Sin(theta) * MathF.Cos(phi), MathF.Cos(theta), MathF.Sin(theta) * MathF.Sin(phi));
            vertices[y * (lon + 1) + x] = new Vertex(normal * radius, normal,
                new Vector2((float)x / lon, 1f - (float)y / lat));
        }

        var indices = new List<uint>(lat * lon * 6);
        for (int y = 0; y < lat; y++)
        for (int x = 0; x < lon; x++)
        {
            uint a = (uint)(y * (lon + 1) + x);
            uint b = a + (uint)(lon + 1);
            indices.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
        }
        return Tag(new Mesh(vertices, indices.ToArray(), generateTangents: true), $"Primitives.Sphere({F(radius)}, {segments})");
    }

    /// <summary>Cylinder along Y, centred on the origin, with end caps.</summary>
    public static Mesh Cylinder(float radius = 0.5f, float height = 1f, int segments = 32)
    {
        int seg = Math.Max(3, segments);
        float h = height * 0.5f;
        var vertices = new List<Vertex>();
        var indices = new List<uint>();

        for (int i = 0; i <= seg; i++)
        {
            float a = MathF.Tau * i / seg;
            var n = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
            float u = (float)i / seg;
            vertices.Add(new Vertex(new Vector3(n.X * radius, -h, n.Z * radius), n, new Vector2(u, 0)));
            vertices.Add(new Vertex(new Vector3(n.X * radius, h, n.Z * radius), n, new Vector2(u, 1)));
        }
        for (int i = 0; i < seg; i++)
        {
            uint b = (uint)(i * 2), t = b + 1, b2 = b + 2, t2 = b + 3;
            indices.AddRange(new[] { b, t, b2, b2, t, t2 });
        }

        AddDisc(vertices, indices, radius, h, seg, up: true);
        AddDisc(vertices, indices, radius, -h, seg, up: false);
        return Tag(new Mesh(vertices.ToArray(), indices.ToArray(), generateTangents: true), $"Primitives.Cylinder({F(radius)}, {F(height)}, {segments})");
    }

    /// <summary>Cone along Y with its base at -height/2 and its apex at +height/2.</summary>
    public static Mesh Cone(float radius = 0.5f, float height = 1f, int segments = 32)
    {
        int seg = Math.Max(3, segments);
        float h = height * 0.5f;
        var vertices = new List<Vertex>();
        var indices = new List<uint>();

        for (int i = 0; i <= seg; i++)
        {
            float a = MathF.Tau * i / seg;
            // Slanted side normal: radial component scaled by height, vertical by radius.
            var n = Vector3.Normalize(new Vector3(MathF.Cos(a) * height, radius, MathF.Sin(a) * height));
            float u = (float)i / seg;
            vertices.Add(new Vertex(new Vector3(MathF.Cos(a) * radius, -h, MathF.Sin(a) * radius), n, new Vector2(u, 0)));
            vertices.Add(new Vertex(new Vector3(0, h, 0), n, new Vector2(u, 1)));
        }
        for (int i = 0; i < seg; i++)
        {
            uint b = (uint)(i * 2), t = b + 1, b2 = b + 2;
            indices.AddRange(new[] { b, t, b2 });
        }

        AddDisc(vertices, indices, radius, -h, seg, up: false);
        return Tag(new Mesh(vertices.ToArray(), indices.ToArray(), generateTangents: true), $"Primitives.Cone({F(radius)}, {F(height)}, {segments})");
    }

    /// <summary>Capsule along Y. <paramref name="height"/> is the total height including both caps.</summary>
    public static Mesh Capsule(float radius = 0.35f, float height = 1.5f, int segments = 24)
    {
        int lon = Math.Max(3, segments);
        int ringsPerHemisphere = Math.Max(2, segments / 4);
        float half = MathF.Max(0f, height * 0.5f - radius);

        var vertices = new List<Vertex>();
        int rows = (ringsPerHemisphere + 1) * 2;
        for (int row = 0; row < rows; row++)
        {
            bool top = row <= ringsPerHemisphere;
            int j = top ? row : row - ringsPerHemisphere - 1;
            float theta = (top ? 0f : MathF.PI * 0.5f) + MathF.PI * 0.5f * j / ringsPerHemisphere;
            float yOffset = top ? half : -half;
            float v = 1f - (float)row / (rows - 1);

            for (int x = 0; x <= lon; x++)
            {
                float phi = MathF.Tau * x / lon;
                var n = new Vector3(MathF.Sin(theta) * MathF.Cos(phi), MathF.Cos(theta), MathF.Sin(theta) * MathF.Sin(phi));
                vertices.Add(new Vertex(n * radius + new Vector3(0, yOffset, 0), n, new Vector2((float)x / lon, v)));
            }
        }

        var indices = new List<uint>();
        for (int row = 0; row < rows - 1; row++)
        for (int x = 0; x < lon; x++)
        {
            uint a = (uint)(row * (lon + 1) + x);
            uint b = a + (uint)(lon + 1);
            indices.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
        }
        return Tag(new Mesh(vertices.ToArray(), indices.ToArray(), generateTangents: true), $"Primitives.Capsule({F(radius)}, {F(height)}, {segments})");
    }

    /// <summary>Torus lying in the XZ plane. <paramref name="majorRadius"/> is the ring, <paramref name="minorRadius"/> the tube.</summary>
    public static Mesh Torus(float majorRadius = 0.6f, float minorRadius = 0.2f, int majorSegments = 40, int minorSegments = 20)
    {
        int maj = Math.Max(3, majorSegments);
        int min = Math.Max(3, minorSegments);

        var vertices = new Vertex[(maj + 1) * (min + 1)];
        for (int i = 0; i <= maj; i++)
        for (int j = 0; j <= min; j++)
        {
            float u = MathF.Tau * i / maj;
            float v = MathF.Tau * j / min;
            var normal = new Vector3(MathF.Cos(v) * MathF.Cos(u), MathF.Sin(v), MathF.Cos(v) * MathF.Sin(u));
            var center = new Vector3(MathF.Cos(u), 0, MathF.Sin(u)) * majorRadius;
            vertices[i * (min + 1) + j] = new Vertex(center + normal * minorRadius, normal,
                new Vector2((float)i / maj, (float)j / min));
        }

        var indices = new List<uint>();
        for (int i = 0; i < maj; i++)
        for (int j = 0; j < min; j++)
        {
            uint a = (uint)(i * (min + 1) + j);
            uint b = a + (uint)(min + 1); // next along the ring
            uint c = a + 1;               // next around the tube
            uint d = b + 1;
            indices.AddRange(new[] { a, c, b, b, c, d });
        }
        return Tag(new Mesh(vertices, indices.ToArray(), generateTangents: true), $"Primitives.Torus({F(majorRadius)}, {F(minorRadius)}, {majorSegments}, {minorSegments})");
    }

    /// <summary>
    /// Terrain in the XZ plane (centred on the origin) whose height at (x, z) comes from <paramref name="height"/>.
    /// Normals come from the height function's slope. <paramref name="color"/> optionally tints vertices
    /// (arguments: x, z, height), e.g. sand below the waterline. UVs span 0..1 over the whole mesh.
    /// </summary>
    public static Mesh Heightmap(float width, float depth, int subdivisions, Func<float, float, float> height,
        Func<float, float, float, Vector4>? color = null)
    {
        int n = Math.Max(1, subdivisions);
        float eps = MathF.Max(width, depth) / n * 0.5f;

        var vertices = new Vertex[(n + 1) * (n + 1)];
        for (int z = 0; z <= n; z++)
        for (int x = 0; x <= n; x++)
        {
            float fx = (float)x / n, fz = (float)z / n;
            float px = (fx - 0.5f) * width, pz = (fz - 0.5f) * depth;
            float h = height(px, pz);

            float dhdx = (height(px + eps, pz) - height(px - eps, pz)) / (2f * eps);
            float dhdz = (height(px, pz + eps) - height(px, pz - eps)) / (2f * eps);
            var normal = Vector3.Normalize(new Vector3(-dhdx, 1f, -dhdz));

            vertices[z * (n + 1) + x] = new Vertex(new Vector3(px, h, pz), normal, new Vector2(fx, 1f - fz),
                color?.Invoke(px, pz, h) ?? Vector4.One);
        }

        var indices = new List<uint>(n * n * 6);
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            uint a = (uint)(z * (n + 1) + x);
            uint b = a + (uint)(n + 1);
            indices.AddRange(new[] { a, b, b + 1, a, b + 1, a + 1 });
        }
        return new Mesh(vertices, indices.ToArray(), generateTangents: true);
    }

    /// <summary>Arrow along +Y starting at the origin (a thin shaft with a cone head); used by the editor's move gizmo.</summary>
    public static Mesh Arrow(float length = 1f, float shaftRadius = 0.02f, float headLength = 0.2f, float headRadius = 0.07f,
        int segments = 12)
    {
        float shaftLength = MathF.Max(length - headLength, 0.001f);
        using var shaft = Cylinder(shaftRadius, shaftLength, segments);
        using var head = Cone(headRadius, headLength, segments);

        var vertices = new List<Vertex>(shaft.Vertices.Length + head.Vertices.Length);
        var indices = new List<uint>(shaft.Indices.Length + head.Indices.Length);
        foreach (var v in shaft.Vertices)
        {
            var copy = v;
            copy.Position.Y += shaftLength * 0.5f;
            vertices.Add(copy);
        }
        indices.AddRange(shaft.Indices);

        uint offset = (uint)vertices.Count;
        foreach (var v in head.Vertices)
        {
            var copy = v;
            copy.Position.Y += shaftLength + headLength * 0.5f;
            vertices.Add(copy);
        }
        indices.AddRange(head.Indices.Select(i => i + offset));
        return new Mesh(vertices.ToArray(), indices.ToArray(), generateTangents: true);
    }

    /// <summary>
    /// A film-camera shape (box body, lens and a viewfinder on top), <paramref name="size"/> units long, looking down
    /// -Z like every object's forward. Marks game cameras in the editor.
    /// </summary>
    public static Mesh CameraBody(float size = 0.6f)
    {
        float s = size / 0.6f; // the shape is designed 0.6 long
        var vertices = new List<Vertex>();
        var indices = new List<uint>();

        using (var body = Cube(1f)) Append(body, Matrix4.CreateScale(0.32f * s, 0.3f * s, 0.4f * s) * Matrix4.CreateTranslation(0f, 0f, 0.1f * s));
        // The cone points up +Y; turned so its tip touches the body and its wide end faces forward (-Z).
        using (var lens = Cone(0.13f * s, 0.2f * s, 20))
            Append(lens, Matrix4.CreateRotationX(MathF.PI / 2f) * Matrix4.CreateTranslation(0f, 0f, -0.2f * s));
        using (var finder = Cylinder(0.06f * s, 0.1f * s, 14))
            Append(finder, Matrix4.CreateRotationX(MathF.PI / 2f) * Matrix4.CreateTranslation(0f, 0.2f * s, 0.15f * s));

        return Tag(new Mesh(vertices.ToArray(), indices.ToArray(), generateTangents: true), $"Primitives.CameraBody({F(size)})");

        void Append(Mesh mesh, Matrix4 transform)
        {
            uint offset = (uint)vertices.Count;
            foreach (var v in mesh.Vertices)
            {
                var copy = v;
                copy.Position = Vector3.TransformPosition(v.Position, transform);
                copy.Normal = Vector3.Normalize(Vector3.TransformNormal(v.Normal, transform));
                vertices.Add(copy);
            }
            indices.AddRange(mesh.Indices.Select(i => i + offset));
        }
    }

    /// <summary>A thin shaft along +Y ending in a cube; the editor's scale gizmo handle.</summary>
    public static Mesh ScaleHandle(float length = 1f, float shaftRadius = 0.02f, float boxSize = 0.14f, int segments = 12)
    {
        float shaftLength = MathF.Max(length - boxSize * 0.5f, 0.001f);
        using var shaft = Cylinder(shaftRadius, shaftLength, segments);
        using var box = Cube(boxSize);

        var vertices = new List<Vertex>(shaft.Vertices.Length + box.Vertices.Length);
        var indices = new List<uint>(shaft.Indices.Length + box.Indices.Length);
        foreach (var v in shaft.Vertices)
        {
            var copy = v;
            copy.Position.Y += shaftLength * 0.5f;
            vertices.Add(copy);
        }
        indices.AddRange(shaft.Indices);

        uint offset = (uint)vertices.Count;
        foreach (var v in box.Vertices)
        {
            var copy = v;
            copy.Position.Y += length - boxSize * 0.5f;
            vertices.Add(copy);
        }
        indices.AddRange(box.Indices.Select(i => i + offset));
        return new Mesh(vertices.ToArray(), indices.ToArray(), generateTangents: true);
    }

    /// <summary>Flat quad in the XY plane facing +Z. The default 2x2 size is a fullscreen quad in clip space.</summary>
    public static Mesh Quad(float width = 2f, float height = 2f)
    {
        float w = width * 0.5f, h = height * 0.5f;
        var vertices = new[]
        {
            new Vertex(new Vector3(-w, -h, 0), Vector3.UnitZ, new Vector2(0, 0)),
            new Vertex(new Vector3(w, -h, 0), Vector3.UnitZ, new Vector2(1, 0)),
            new Vertex(new Vector3(w, h, 0), Vector3.UnitZ, new Vector2(1, 1)),
            new Vertex(new Vector3(-w, h, 0), Vector3.UnitZ, new Vector2(0, 1)),
        };
        return Tag(new Mesh(vertices, new uint[] { 0, 1, 2, 0, 2, 3 }, generateTangents: true), $"Primitives.Quad({F(width)}, {F(height)})");
    }

    /// <summary>Square grid of lines in the XZ plane (use with an unlit material).</summary>
    public static Mesh Grid(float size = 20f, int divisions = 20)
    {
        int n = Math.Max(1, divisions);
        float half = size * 0.5f;
        var line = new Vector4(0.45f, 0.45f, 0.45f, 1f);
        var axis = new Vector4(0.75f, 0.75f, 0.75f, 1f);

        var vertices = new List<Vertex>();
        var indices = new List<uint>();
        for (int i = 0; i <= n; i++)
        {
            float p = -half + size * i / n;
            var color = i == n / 2 && n % 2 == 0 ? axis : line;
            AddLine(vertices, indices, new Vector3(p, 0, -half), new Vector3(p, 0, half), color);
            AddLine(vertices, indices, new Vector3(-half, 0, p), new Vector3(half, 0, p), color);
        }
        return Tag(new Mesh(vertices.ToArray(), indices.ToArray(), PrimitiveType.Lines), $"Primitives.Grid({F(size)}, {divisions})");
    }

    /// <summary>A single line segment (use with an unlit material).</summary>
    public static Mesh Line(Vector3 a, Vector3 b, Color4? color = null)
    {
        var c = color ?? Color4.White;
        var vertices = new List<Vertex>();
        var indices = new List<uint>();
        AddLine(vertices, indices, a, b, new Vector4(c.R, c.G, c.B, c.A));
        return new Mesh(vertices.ToArray(), indices.ToArray(), PrimitiveType.Lines);
    }

    /// <summary>RGB axis gizmo: X red, Y green, Z blue (use with an unlit material).</summary>
    public static Mesh Axes(float length = 1f)
    {
        var vertices = new List<Vertex>();
        var indices = new List<uint>();
        AddLine(vertices, indices, Vector3.Zero, Vector3.UnitX * length, new Vector4(1, 0.2f, 0.2f, 1));
        AddLine(vertices, indices, Vector3.Zero, Vector3.UnitY * length, new Vector4(0.2f, 1, 0.2f, 1));
        AddLine(vertices, indices, Vector3.Zero, Vector3.UnitZ * length, new Vector4(0.3f, 0.5f, 1, 1));
        return Tag(new Mesh(vertices.ToArray(), indices.ToArray(), PrimitiveType.Lines), $"Primitives.Axes({F(length)})");
    }

    private static void AddLine(List<Vertex> vertices, List<uint> indices, Vector3 a, Vector3 b, Vector4 color)
    {
        uint start = (uint)vertices.Count;
        vertices.Add(new Vertex(a, Vector3.UnitY, Vector2.Zero, color));
        vertices.Add(new Vertex(b, Vector3.UnitY, Vector2.Zero, color));
        indices.Add(start);
        indices.Add(start + 1);
    }

    /// <summary>Flat disc at height <paramref name="y"/> facing +Y (<paramref name="up"/>) or -Y.</summary>
    private static void AddDisc(List<Vertex> vertices, List<uint> indices, float radius, float y, int segments, bool up)
    {
        var normal = up ? Vector3.UnitY : -Vector3.UnitY;
        uint center = (uint)vertices.Count;
        vertices.Add(new Vertex(new Vector3(0, y, 0), normal, new Vector2(0.5f, 0.5f)));
        for (int i = 0; i <= segments; i++)
        {
            float a = MathF.Tau * i / segments;
            vertices.Add(new Vertex(new Vector3(MathF.Cos(a) * radius, y, MathF.Sin(a) * radius), normal,
                new Vector2(0.5f + MathF.Cos(a) * 0.5f, 0.5f + MathF.Sin(a) * 0.5f)));
        }
        for (int i = 0; i < segments; i++)
        {
            uint r0 = center + 1 + (uint)i, r1 = r0 + 1;
            if (up) indices.AddRange(new[] { center, r1, r0 });
            else indices.AddRange(new[] { center, r0, r1 });
        }
    }
}
