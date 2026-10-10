using GT67.Utils;
using OpenTK.Graphics.OpenGL4;

namespace GT67.Core;

/// <summary>How a colour target stores depth.</summary>
public enum DepthMode
{
    None,
    Renderbuffer,
    /// <summary>Sampleable depth texture (ignored for multisampled targets, which use a renderbuffer).</summary>
    Texture,
}

/// <summary>
/// Reusable FBO wrapper. Colour targets are RGBA8/RGBA16F (texture, or multisampled renderbuffer when
/// <c>samples &gt; 0</c>); depth-only targets expose a depth texture for shadow mapping.
/// </summary>
public sealed class Framebuffer : IDisposable
{
    private readonly bool _depthOnly;
    private readonly bool _hdr;
    private readonly DepthMode _depthMode;
    private int _colorRenderbuffer, _depthRenderbuffer;
    private bool _disposed;

    public int Handle { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Samples { get; }

    /// <summary>Colour texture (0 for multisampled or depth-only targets).</summary>
    public int ColorTexture { get; private set; }

    /// <summary>Depth texture (depth-only targets only).</summary>
    public int DepthTexture { get; private set; }

    public bool IsComplete { get; private set; }

    private Framebuffer(int width, int height, int samples, bool hdr, bool depthOnly, DepthMode depthMode)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Samples = samples;
        _hdr = hdr;
        _depthOnly = depthOnly;
        _depthMode = samples > 0 && depthMode == DepthMode.Texture ? DepthMode.Renderbuffer : depthMode;
        Allocate();
    }

    /// <summary>Colour + depth target; multisampled when <paramref name="samples"/> &gt; 0.</summary>
    public static Framebuffer CreateColor(int width, int height, int samples = 0, bool hdr = true,
        DepthMode depth = DepthMode.Renderbuffer) =>
        new(width, height, samples, hdr, depthOnly: false, depth);

    /// <summary>Depth-only target (shadow maps).</summary>
    public static Framebuffer CreateDepth(int width, int height) =>
        new(width, height, 0, hdr: false, depthOnly: true, DepthMode.Texture);

    private void Allocate()
    {
        Handle = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, Handle);

        if (_depthOnly)
        {
            DepthTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, DepthTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent32f, Width, Height, 0,
                PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, new[] { 1f, 1f, 1f, 1f });
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                TextureTarget.Texture2D, DepthTexture, 0);
            GL.DrawBuffer(DrawBufferMode.None);
            GL.ReadBuffer(ReadBufferMode.None);
        }
        else
        {
            var format = _hdr ? RenderbufferStorage.Rgba16f : RenderbufferStorage.Rgba8;
            if (Samples > 0)
            {
                _colorRenderbuffer = GL.GenRenderbuffer();
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _colorRenderbuffer);
                GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, Samples, format, Width, Height);
                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                    RenderbufferTarget.Renderbuffer, _colorRenderbuffer);
            }
            else
            {
                ColorTexture = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, ColorTexture);
                GL.TexImage2D(TextureTarget.Texture2D, 0,
                    _hdr ? PixelInternalFormat.Rgba16f : PixelInternalFormat.Rgba8, Width, Height, 0,
                    PixelFormat.Rgba, _hdr ? PixelType.Float : PixelType.UnsignedByte, IntPtr.Zero);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                    TextureTarget.Texture2D, ColorTexture, 0);
            }

            if (_depthMode == DepthMode.Texture)
            {
                DepthTexture = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, DepthTexture);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24, Width, Height, 0,
                    PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                    TextureTarget.Texture2D, DepthTexture, 0);
            }
            else if (_depthMode == DepthMode.Renderbuffer)
            {
                _depthRenderbuffer = GL.GenRenderbuffer();
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthRenderbuffer);
                if (Samples > 0)
                    GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, Samples,
                        RenderbufferStorage.DepthComponent24, Width, Height);
                else
                    GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, Width, Height);
                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                    RenderbufferTarget.Renderbuffer, _depthRenderbuffer);
            }
        }

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        IsComplete = status == FramebufferErrorCode.FramebufferComplete;
        if (!IsComplete) Log.Warn($"Framebuffer {Width}x{Height} (samples {Samples}) incomplete: {status}");

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void Free()
    {
        if (Handle != 0) GL.DeleteFramebuffer(Handle);
        if (ColorTexture != 0) GL.DeleteTexture(ColorTexture);
        if (DepthTexture != 0) GL.DeleteTexture(DepthTexture);
        if (_colorRenderbuffer != 0) GL.DeleteRenderbuffer(_colorRenderbuffer);
        if (_depthRenderbuffer != 0) GL.DeleteRenderbuffer(_depthRenderbuffer);
        Handle = ColorTexture = DepthTexture = _colorRenderbuffer = _depthRenderbuffer = 0;
    }

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height) return;

        Free();
        Width = width;
        Height = height;
        Allocate();
    }

    /// <summary>Binds as the draw target and sets the viewport to cover it.</summary>
    public void Bind()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, Handle);
        GL.Viewport(0, 0, Width, Height);
    }

    /// <summary>Binds the window's framebuffer and limits drawing to a rectangle of it (x, y from the bottom left).</summary>
    public static void BindDefault(int x, int y, int width, int height)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(x, y, width, height);
    }

    /// <summary>
    /// Resolves colour (and depth when the target has a depth texture) into another framebuffer,
    /// e.g. multisampled to single-sampled.
    /// </summary>
    public void BlitColorTo(Framebuffer target)
    {
        var mask = ClearBufferMask.ColorBufferBit;
        if (target._depthMode == DepthMode.Texture && _depthMode != DepthMode.None)
            mask |= ClearBufferMask.DepthBufferBit;

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, Handle);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, target.Handle);
        GL.BlitFramebuffer(0, 0, Width, Height, 0, 0, target.Width, target.Height, mask, BlitFramebufferFilter.Nearest);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void BindColorTexture(TextureUnit unit)
    {
        GL.ActiveTexture(unit);
        GL.BindTexture(TextureTarget.Texture2D, ColorTexture);
    }

    public void BindDepthTexture(TextureUnit unit)
    {
        GL.ActiveTexture(unit);
        GL.BindTexture(TextureTarget.Texture2D, DepthTexture);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Free();
    }
}
