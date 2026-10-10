using System.Runtime.InteropServices;
using GT67.Core;
using GT67.Utils;
using OpenTK.Graphics.OpenGL4;
using SkiaSharp;

namespace GT67.Gui;

/// <summary>
/// Where the UI is drawn. Skia renders on the CPU into a pixel buffer (anti-aliased shapes and real font text), the
/// buffer is uploaded to a texture and composited over the finished frame with alpha blending. Doing it this way keeps
/// Skia completely separate from the engine's OpenGL state.
/// </summary>
public sealed class UiSurface : IDisposable
{
    private readonly Shader _shader;
    private readonly Mesh _quad = Primitives.Quad();
    private SKSurface? _surface;
    private IntPtr _pixels;
    private int _texture;
    private bool _disposed;

    public UiSurface(string shaderDirectory)
    {
        _shader = new Shader(Path.Combine(shaderDirectory, "post.vert"), Path.Combine(shaderDirectory, "ui.frag"));
    }

    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>The canvas to draw the UI on (valid after <see cref="Begin"/>).</summary>
    public SKCanvas Canvas => _surface?.Canvas ?? throw new InvalidOperationException("Call Begin first.");

    public Shader Shader => _shader;

    /// <summary>Starts a frame at the given pixel size (recreating the buffers when the size changed) and clears it.</summary>
    public void Begin(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (_surface == null || width != Width || height != Height) Recreate(width, height);
        Canvas.Clear(SKColors.Transparent);
    }

    private void Recreate(int width, int height)
    {
        _surface?.Dispose();
        if (_pixels != IntPtr.Zero) Marshal.FreeHGlobal(_pixels);
        if (_texture != 0) GL.DeleteTexture(_texture);

        Width = width;
        Height = height;
        _pixels = Marshal.AllocHGlobal(width * height * 4);

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        _surface = SKSurface.Create(info, _pixels, width * 4)
                   ?? throw new InvalidOperationException("Skia could not create the UI surface.");

        _texture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _texture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
    }

    /// <summary>Uploads what was drawn and blends it over the whole default framebuffer.</summary>
    public void Present()
    {
        if (_surface == null) return;
        _surface.Canvas.Flush();

        GL.BindTexture(TextureTarget.Texture2D, _texture);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, Width, Height, PixelFormat.Rgba, PixelType.UnsignedByte, _pixels);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, Width, Height);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.ScissorTest);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha); // premultiplied alpha
        GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);

        _shader.Use();
        _shader.SetInt("uUi", 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _texture);
        _quad.Draw();

        GL.Disable(EnableCap.Blend);
        GL.Enable(EnableCap.DepthTest);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _surface?.Dispose();
        if (_pixels != IntPtr.Zero) Marshal.FreeHGlobal(_pixels);
        if (_texture != 0) GL.DeleteTexture(_texture);
        _quad.Dispose();
        _shader.Dispose();
    }
}
