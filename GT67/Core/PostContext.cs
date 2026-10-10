using OpenTK.Graphics.OpenGL4;

namespace GT67.Core;

/// <summary>Per-frame information and helpers handed to every <see cref="PostEffect"/>.</summary>
public sealed class PostContext
{
    private readonly string _shaderDirectory;
    private readonly Mesh _quad;

    internal PostContext(string shaderDirectory, Mesh quad)
    {
        _shaderDirectory = shaderDirectory;
        _quad = quad;
    }

    /// <summary>Size of the full-resolution render target in pixels.</summary>
    public int Width { get; internal set; }
    public int Height { get; internal set; }

    /// <summary>Resolved scene depth (non-linear, 1.0 where nothing was drawn, e.g. the sky).</summary>
    public int DepthTexture { get; internal set; }

    /// <summary>Sun shadow map (depth texture) and its world-to-light matrix; ShadowTexture is 0 when shadows are off.</summary>
    /// <summary>Renderer quality level, 0 (best) to 3 (fastest): effects with a cost knob should lower it.</summary>
    public int Quality { get; internal set; }

    public int ShadowTexture { get; internal set; }
    public OpenTK.Mathematics.Matrix4 ShadowMatrix { get; internal set; }

    public Camera Camera { get; internal set; } = null!;
    public Scene Scene { get; internal set; } = null!;

    /// <summary>Seconds since the renderer started, and since the previous frame.</summary>
    public float Time { get; internal set; }
    public float DeltaTime { get; internal set; }

    /// <summary>Lower-left corner, in window pixels, of the area of the window's framebuffer the result is drawn into.</summary>
    internal OpenTK.Mathematics.Vector2i OutputOffset { get; set; }

    /// <summary>Where this effect's result must end up; null means the window's framebuffer.</summary>
    internal Framebuffer? Output { get; set; }

    /// <summary>Binds the chain's output target (use after rendering into internal buffers).</summary>
    public void BindOutput()
    {
        if (Output != null) Output.Bind();
        else Framebuffer.BindDefault(OutputOffset.X, OutputOffset.Y, Width, Height);
    }

    /// <summary>Compiles the shared fullscreen vertex shader with Shaders/&lt;name&gt;.frag.</summary>
    public Shader LoadShader(string fragmentName) =>
        new(Path.Combine(_shaderDirectory, "post.vert"), Path.Combine(_shaderDirectory, fragmentName + ".frag"));

    public void BindTexture(TextureUnit unit, int texture)
    {
        GL.ActiveTexture(unit);
        GL.BindTexture(TextureTarget.Texture2D, texture);
    }

    public void DrawQuad() => _quad.Draw();
}
