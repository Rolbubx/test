using GT67.Utils;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>GLSL program with cached uniform locations and hot reload.</summary>
public sealed class Shader : IDisposable
{
    private const string VersionHeader = "#version 410 core\n";

    private readonly Dictionary<string, int> _uniforms = new();
    private readonly string? _vertPath;
    private readonly string? _fragPath;
    private bool _disposed;

    /// <summary>The compiler's message from the most recent failed build (null after a good one); used to show errors in the editor.</summary>
    public static string? LastError { get; private set; }

    public int Handle { get; private set; }
    public string Name { get; }

    /// <summary>Loads and compiles a program from two files (hot-reloadable).</summary>
    public Shader(string vertPath, string fragPath)
    {
        _vertPath = vertPath;
        _fragPath = fragPath;
        Name = Path.GetFileNameWithoutExtension(fragPath);
        Handle = Build(File.ReadAllText(vertPath), File.ReadAllText(fragPath), Name)
                 ?? throw new InvalidOperationException($"Shader '{Name}' failed to compile (see log).");
    }

    private Shader(string name, string vertSource, string fragSource)
    {
        Name = name;
        Handle = Build(vertSource, fragSource, name)
                 ?? throw new InvalidOperationException($"Shader '{name}' failed to compile (see log).");
    }

    /// <summary>Compiles a program from source strings (no hot reload).</summary>
    public static Shader FromSource(string name, string vertSource, string fragSource) =>
        new(name, vertSource, fragSource);

    public void Use() => GL.UseProgram(Handle);

    /// <summary>Recompiles from disk. The previous program stays active if compilation fails.</summary>
    public bool Reload()
    {
        if (_vertPath == null || _fragPath == null) return false;

        try
        {
            var vert = File.ReadAllText(_vertPath);
            var frag = File.ReadAllText(_fragPath);
            int program = Build(vert, frag, Name) ?? 0;
            if (program == 0) return false;

            GL.DeleteProgram(Handle);
            Handle = program;
            _uniforms.Clear();
            Log.Info($"Shader '{Name}' reloaded.");
            return true;
        }
        catch (IOException e)
        {
            Log.Error($"Shader '{Name}' reload failed: {e.Message}");
            return false;
        }
    }

    private static int? Build(string vertSource, string fragSource, string name)
    {
        LastError = null;
        int vs = Compile(ShaderType.VertexShader, vertSource, name);
        int fs = Compile(ShaderType.FragmentShader, fragSource, name);
        if (vs == 0 || fs == 0)
        {
            if (vs != 0) GL.DeleteShader(vs);
            if (fs != 0) GL.DeleteShader(fs);
            return null;
        }

        int program = GL.CreateProgram();
        GL.AttachShader(program, vs);
        GL.AttachShader(program, fs);
        GL.LinkProgram(program);

        GL.DetachShader(program, vs);
        GL.DetachShader(program, fs);
        GL.DeleteShader(vs);
        GL.DeleteShader(fs);

        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int ok);
        if (ok == 0)
        {
            Log.Error($"Shader '{name}' link error:\n{GL.GetProgramInfoLog(program)}");
            GL.DeleteProgram(program);
            return null;
        }
        return program;
    }

    private static int Compile(ShaderType type, string source, string name)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, VersionHeader + source);
        GL.CompileShader(shader);

        GL.GetShader(shader, ShaderParameter.CompileStatus, out int ok);
        if (ok == 0)
        {
            string kind = type == ShaderType.VertexShader ? "vertex" : "fragment";
            LastError = GL.GetShaderInfoLog(shader);
            Log.Error($"Shader '{name}' {kind} compile error (line numbers are offset by +1 for the version header):\n" +
                      GL.GetShaderInfoLog(shader));
            GL.DeleteShader(shader);
            return 0;
        }
        return shader;
    }

    private int Location(string name)
    {
        if (_uniforms.TryGetValue(name, out int loc)) return loc;
        loc = GL.GetUniformLocation(Handle, name);
        _uniforms[name] = loc;
        return loc;
    }

    // ProgramUniform* is core in 4.1 and does not require the program to be bound.
    public void SetInt(string name, int value) => GL.ProgramUniform1(Handle, Location(name), value);
    public void SetBool(string name, bool value) => GL.ProgramUniform1(Handle, Location(name), value ? 1 : 0);
    public void SetFloat(string name, float value) => GL.ProgramUniform1(Handle, Location(name), value);
    public void SetVector2(string name, Vector2 v) => GL.ProgramUniform2(Handle, Location(name), v.X, v.Y);
    public void SetVector3(string name, Vector3 v) => GL.ProgramUniform3(Handle, Location(name), v.X, v.Y, v.Z);
    public void SetVector4(string name, Vector4 v) => GL.ProgramUniform4(Handle, Location(name), v.X, v.Y, v.Z, v.W);

    public void SetMatrix3(string name, Matrix3 m) =>
        GL.ProgramUniformMatrix3(Handle, Location(name), false, ref m);

    public void SetMatrix4(string name, Matrix4 m) =>
        GL.ProgramUniformMatrix4(Handle, Location(name), false, ref m);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GL.DeleteProgram(Handle);
    }
}
