using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>Depth map rendered from the directional light with an orthographic projection.</summary>
public sealed class ShadowMap : IDisposable
{
    private readonly Framebuffer _framebuffer;

    public int Resolution { get; }

    /// <summary>World size of one texel of the shadow map (set by <see cref="Begin"/>).</summary>
    public float TexelSize { get; private set; } = 0.05f;

    /// <summary>World to light clip space (row-vector convention, like every other matrix here).</summary>
    public Matrix4 LightSpaceMatrix { get; private set; } = Matrix4.Identity;

    public ShadowMap(int resolution = 2048)
    {
        Resolution = resolution;
        _framebuffer = Framebuffer.CreateDepth(resolution, resolution);
    }

    /// <summary>Computes the light matrix, binds the depth target and clears it.</summary>
    public void Begin(Sun light)
    {
        float size = MathF.Max(1f, light.ShadowSize);
        var direction = light.Direction;
        var up = MathF.Abs(direction.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        TexelSize = 2f * size / Resolution;

        // The light looks from a fixed place; only the window moves, in whole texels. A shadow edge then stays on the
        // same texels while the camera moves instead of crawling and shimmering.
        var view = Matrix4.LookAt(-direction * size * 2f, Vector3.Zero, up);
        var center = Vector4.TransformRow(new Vector4(light.ShadowCenter, 1f), view);
        float cx = MathF.Round(center.X / TexelSize) * TexelSize;
        float cy = MathF.Round(center.Y / TexelSize) * TexelSize;
        float depth = -center.Z;
        var projection = Matrix4.CreateOrthographicOffCenter(cx - size, cx + size, cy - size, cy + size,
            MathF.Max(0.1f, depth - size * 2f), depth + size * 2f);
        LightSpaceMatrix = view * projection;

        _framebuffer.Bind();
        GL.Enable(EnableCap.DepthTest);
        GL.DepthMask(true);
        GL.Clear(ClearBufferMask.DepthBufferBit);
    }

    public int DepthTexture => _framebuffer.DepthTexture;

    public void Bind(TextureUnit unit) => _framebuffer.BindDepthTexture(unit);

    public void Dispose() => _framebuffer.Dispose();
}
