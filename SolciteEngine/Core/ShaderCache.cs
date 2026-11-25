using System.Security.Cryptography;
using System.Text;
using SolciteEngine.Utils;
using OpenTK.Graphics.OpenGL4;

namespace SolciteEngine.Core;

/// <summary>
/// An on-disk cache of linked GPU programs (<c>glGetProgramBinary</c>, core since OpenGL 4.1). A shader that was compiled once
/// is loaded from its binary the next time instead of being compiled again, which makes start-up much faster. The cache is
/// per user (<c>%LocalAppData%/SolciteEngine/shader-cache/&lt;driver&gt;/</c>), keyed by the shader's source and by the graphics
/// driver (a new driver version gets a new folder, since binaries are driver specific). It can be deleted at any time.
/// </summary>
public static class ShaderCache
{
    private static string? _folder;
    private static bool _checked, _supported;

    /// <summary>Turn the cache off (every shader is compiled from source).</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>How many programs were loaded from the cache, compiled, and written to it since start-up.</summary>
    public static int Hits { get; private set; }
    public static int Misses { get; private set; }
    public static int Stored { get; private set; }

    /// <summary>The per-user folder of the engine's data (shader cache, setup marker, ...).</summary>
    public static string UserDataFolder
    {
        get
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root))   // no home folder known (some service accounts, containers)
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                root = Path.Combine(string.IsNullOrEmpty(home) ? Path.GetTempPath() : home, ".local", "share");
            }
            return Path.Combine(root, "SolciteEngine");
        }
    }

    /// <summary>Identifies the graphics driver: vendor, renderer and version. Cached binaries are valid only for the same one.</summary>
    public static string DriverId =>
        $"{GL.GetString(StringName.Vendor)}|{GL.GetString(StringName.Renderer)}|{GL.GetString(StringName.Version)}";

    /// <summary>The cache folder of the current driver (created on first use), or null if the cache is unavailable.</summary>
    public static string? Folder
    {
        get
        {
            if (!Enabled) return null;
            if (!_checked)
            {
                _checked = true;
                GL.GetInteger(GetPName.NumProgramBinaryFormats, out int formats);
                _supported = formats > 0;
                try
                {
                    if (_supported)
                    {
                        _folder = Path.Combine(UserDataFolder, "shader-cache", Hash(DriverId)[..16]);
                        Directory.CreateDirectory(_folder);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Log.Warn($"Shader cache unavailable: {e.Message}");
                    _folder = null;
                }
            }
            return _folder;
        }
    }

    /// <summary>True if the driver can save program binaries (and the cache folder can be written).</summary>
    public static bool Supported => Folder != null;

    /// <summary>The cached programs of the current driver.</summary>
    public static int EntryCount =>
        Folder is { } f && Directory.Exists(f) ? Directory.EnumerateFiles(f, "*.bin").Count() : 0;

    /// <summary>Deletes every cached program (all drivers).</summary>
    public static void Clear()
    {
        try
        {
            string root = Path.Combine(UserDataFolder, "shader-cache");
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            _checked = false;
            _folder = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not clear the shader cache: {e.Message}");
        }
    }

    internal static string KeyOf(string vertexSource, string fragmentSource) => Hash(vertexSource + "\0" + fragmentSource);

    /// <summary>Returns a linked program built from the cached binary of <paramref name="key"/>, or 0.</summary>
    internal static int TryLoad(string key)
    {
        if (Folder is not { } folder) return 0;
        string file = Path.Combine(folder, key + ".bin");
        if (!File.Exists(file)) return 0;

        int program = 0;
        try
        {
            byte[] data = File.ReadAllBytes(file);
            if (data.Length <= 4) return 0;
            var format = (BinaryFormat)BitConverter.ToInt32(data, 0);

            program = GL.CreateProgram();
            unsafe
            {
                fixed (byte* body = &data[4]) GL.ProgramBinary(program, format, (IntPtr)body, data.Length - 4);
            }
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int ok);
            if (ok != 0)
            {
                Hits++;
                return program;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // unreadable: compile from source instead
        }

        if (program != 0) GL.DeleteProgram(program);
        try { File.Delete(file); } catch (IOException) { /* stale entry the driver refused: leave it */ }
        return 0;
    }

    /// <summary>Writes the binary of a program that was linked with <c>ProgramBinaryRetrievableHint</c>.</summary>
    internal static void Store(string key, int program)
    {
        Misses++;
        if (Folder is not { } folder) return;
        try
        {
            GL.GetProgram(program, GetProgramParameterName.ProgramBinaryLength, out int length);
            if (length <= 0) return;

            var data = new byte[length + 4];
            BinaryFormat format;
            int written;
            unsafe
            {
                fixed (byte* body = &data[4]) GL.GetProgramBinary(program, length, out written, out format, (IntPtr)body);
            }
            if (written <= 0) return;
            BitConverter.GetBytes((int)format).CopyTo(data, 0);

            string file = Path.Combine(folder, key + ".bin");
            string temp = file + ".tmp";
            File.WriteAllBytes(temp, data.AsSpan(0, written + 4).ToArray());
            File.Move(temp, file, overwrite: true);
            Stored++;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not write the shader cache: {e.Message}");
        }
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
