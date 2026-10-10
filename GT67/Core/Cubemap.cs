using GT67.Utils;
using OpenTK.Graphics.OpenGL4;
using StbImageSharp;

namespace GT67.Core;

/// <summary>Cube map texture, used for the skybox.</summary>
public sealed class Cubemap : IDisposable
{
    // Order matches TextureTarget.TextureCubeMapPositiveX + i.
    public static readonly string[] FaceNames = { "right", "left", "top", "bottom", "front", "back" };
    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga" };

    private bool _disposed;

    public int Handle { get; }

    /// <summary>The folder the faces were loaded from (set by <see cref="TryFromFolder"/>).</summary>
    public string? SourceDirectory { get; private set; }

    private Cubemap(int handle) => Handle = handle;

    /// <summary>Loads six face files (right, left, top, bottom, front, back) in the given order.</summary>
    public static Cubemap FromFiles(IReadOnlyList<string> faces)
    {
        if (faces.Count != 6) throw new ArgumentException("A cubemap needs exactly 6 faces.", nameof(faces));

        StbImage.stbi_set_flip_vertically_on_load(0); // cube maps use a top-left origin
        int handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.TextureCubeMap, handle);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

        for (int i = 0; i < 6; i++)
        {
            using var stream = File.OpenRead(faces[i]);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            GL.TexImage2D(TextureTarget.TextureCubeMapPositiveX + i, 0, PixelInternalFormat.Srgb8Alpha8,
                image.Width, image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);
        }

        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
        return new Cubemap(handle);
    }

    /// <summary>
    /// Looks for right/left/top/bottom/front/back with a png/jpg/bmp/tga extension in <paramref name="directory"/>.
    /// Returns null (with a warning) when any face is missing.
    /// </summary>
    public static Cubemap? TryFromFolder(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Log.Warn($"Skybox folder '{directory}' not found.");
            return null;
        }

        var faces = new List<string>();
        foreach (var name in FaceNames)
        {
            var file = Extensions.Select(ext => Path.Combine(directory, name + ext)).FirstOrDefault(File.Exists);
            if (file == null)
            {
                Log.Warn($"Skybox face '{name}' missing in '{directory}'.");
                return null;
            }
            faces.Add(file);
        }

        try
        {
            var cubemap = FromFiles(faces);
            cubemap.SourceDirectory = directory;
            return cubemap;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            Log.Warn($"Skybox failed to load: {e.Message}");
            return null;
        }
    }

    public void Bind(TextureUnit unit)
    {
        GL.ActiveTexture(unit);
        GL.BindTexture(TextureTarget.TextureCubeMap, Handle);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GL.DeleteTexture(Handle);
    }
}
