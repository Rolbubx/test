using GT67.Utils;
using OpenTK.Graphics.OpenGL4;
using StbImageSharp;

namespace GT67.Core;

/// <summary>2D texture with mipmaps and configurable wrap/filter.</summary>
public sealed class Texture : IDisposable
{
    private bool _disposed;

    public int Handle { get; }
    public int Width { get; }
    public int Height { get; }
    public string Path { get; }

    /// <summary>Where this texture came from, as C# code that recreates it (e.g. "Texture.Checkerboard(...)"); null if unknown.</summary>
    public string? Origin { get; set; }

    private Texture(int handle, int width, int height, string path)
    {
        Handle = handle;
        Width = width;
        Height = height;
        Path = path;
    }

    /// <summary>
    /// Loads an image file. Colour textures should use <paramref name="srgb"/> = true,
    /// data textures (normal, specular) false.
    /// </summary>
    public static Texture FromFile(string path, bool srgb = true,
        TextureWrapMode wrap = TextureWrapMode.Repeat,
        TextureMinFilter minFilter = TextureMinFilter.LinearMipmapLinear,
        TextureMagFilter magFilter = TextureMagFilter.Linear)
    {
        StbImage.stbi_set_flip_vertically_on_load(1); // GL's v axis points up
        using var stream = File.OpenRead(path);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        return FromPixels(image.Data, image.Width, image.Height, path, srgb, wrap, minFilter, magFilter);
    }

    /// <summary>Creates a texture from tightly packed RGBA8 pixels.</summary>
    public static Texture FromPixels(byte[] rgba, int width, int height, string name = "<memory>", bool srgb = true,
        TextureWrapMode wrap = TextureWrapMode.Repeat,
        TextureMinFilter minFilter = TextureMinFilter.LinearMipmapLinear,
        TextureMagFilter magFilter = TextureMagFilter.Linear)
    {
        int handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, handle);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage2D(TextureTarget.Texture2D, 0,
            srgb ? PixelInternalFormat.Srgb8Alpha8 : PixelInternalFormat.Rgba8,
            width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);

        var texture = new Texture(handle, width, height, name);
        texture.SetWrap(wrap);
        texture.SetFilter(minFilter, magFilter);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        return texture;
    }

    /// <summary>Procedural two-colour checkerboard, handy as a placeholder.</summary>
    public static Texture Checkerboard(int size = 256, int cells = 8,
        (byte r, byte g, byte b) a = default, (byte r, byte g, byte b) b = default)
    {
        if (a == default) a = (230, 230, 230);
        if (b == default) b = (70, 70, 80);

        var pixels = new byte[size * size * 4];
        int cellSize = Math.Max(1, size / cells);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var c = ((x / cellSize) + (y / cellSize)) % 2 == 0 ? a : b;
            int i = (y * size + x) * 4;
            pixels[i] = c.r;
            pixels[i + 1] = c.g;
            pixels[i + 2] = c.b;
            pixels[i + 3] = 255;
        }
        var texture = FromPixels(pixels, size, size, "<checkerboard>");
        texture.Origin = $"Texture.Checkerboard({size}, {cells}, ({a.r}, {a.g}, {a.b}), ({b.r}, {b.g}, {b.b}))";
        return texture;
    }

    public void SetWrap(TextureWrapMode wrap)
    {
        GL.BindTexture(TextureTarget.Texture2D, Handle);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)wrap);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)wrap);
    }

    public void SetFilter(TextureMinFilter min, TextureMagFilter mag)
    {
        GL.BindTexture(TextureTarget.Texture2D, Handle);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)min);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)mag);
    }

    public void Bind(TextureUnit unit)
    {
        GL.ActiveTexture(unit);
        GL.BindTexture(TextureTarget.Texture2D, Handle);
    }

    /// <summary>
    /// Anisotropic filtering: keeps the texture sharp when seen at a grazing angle (a floor or water far away).
    /// Clamped to what the GPU supports; does nothing where it is not available.
    /// </summary>
    public void SetMaxAnisotropy(float level)
    {
        const TextureParameterName maxAnisotropy = (TextureParameterName)0x84FE;   // GL_TEXTURE_MAX_ANISOTROPY
        const GetPName maxSupported = (GetPName)0x84FF;                             // GL_MAX_TEXTURE_MAX_ANISOTROPY
        GL.GetFloat(maxSupported, out float supported);   // stays 0 if the GPU does not know it
        if (supported <= 1f) return;

        GL.BindTexture(TextureTarget.Texture2D, Handle);
        GL.TexParameter(TextureTarget.Texture2D, maxAnisotropy, Math.Clamp(level, 1f, supported));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GL.DeleteTexture(Handle);
    }
}
