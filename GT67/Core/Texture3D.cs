using OpenTK.Graphics.OpenGL4;

namespace GT67.Core;

/// <summary>Tiling RGBA8 3D texture (used for cloud noise).</summary>
public sealed class Texture3D : IDisposable
{
    private bool _disposed;

    public int Handle { get; }
    public int Size { get; }

    private Texture3D(int handle, int size)
    {
        Handle = handle;
        Size = size;
    }

    /// <summary>Creates a cube-shaped texture from <c>size^3</c> tightly packed RGBA8 voxels (x fastest).</summary>
    public static Texture3D FromRgba(byte[] rgba, int size, bool mipmaps = true)
    {
        int handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture3D, handle);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage3D(TextureTarget.Texture3D, 0, PixelInternalFormat.Rgba8, size, size, size, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, rgba);

        GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapR, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMinFilter,
            (int)(mipmaps ? TextureMinFilter.LinearMipmapLinear : TextureMinFilter.Linear));
        if (mipmaps) GL.GenerateMipmap(GenerateMipmapTarget.Texture3D);

        return new Texture3D(handle, size);
    }

    public void Bind(TextureUnit unit)
    {
        GL.ActiveTexture(unit);
        GL.BindTexture(TextureTarget.Texture3D, Handle);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GL.DeleteTexture(Handle);
    }
}
