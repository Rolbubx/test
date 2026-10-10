using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>Vertex layout: position, normal, UV, tangent, colour (locations 0..4).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 UV;
    public Vector3 Tangent;
    public Vector4 Color;

    public static readonly int SizeInBytes = Marshal.SizeOf<Vertex>();

    public Vertex(Vector3 position, Vector3 normal, Vector2 uv)
    {
        Position = position;
        Normal = normal;
        UV = uv;
        Tangent = Vector3.UnitX;
        Color = Vector4.One;
    }

    public Vertex(Vector3 position, Vector3 normal, Vector2 uv, Vector4 color) : this(position, normal, uv)
    {
        Color = color;
    }
}
