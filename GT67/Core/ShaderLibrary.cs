using System.Globalization;
using System.Text;
using GT67.Utils;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>What kind of value a shader parameter holds.</summary>
public enum ShaderParamKind { Float, Color, Vec2, Vec3 }

/// <summary>A <c>param</c> line of a .shader file: a named value you can change per object.</summary>
public sealed class ShaderParam
{
    public string Name { get; init; } = "";
    public ShaderParamKind Kind { get; init; }
    public Vector4 Default { get; init; }
}

/// <summary>A shader written in the small GT67 shader language (a <c>.shader</c> file), compiled into the lit shader.</summary>
public sealed class CustomShader
{
    public string Name { get; init; } = "";
    public string File { get; init; } = "";
    public List<ShaderParam> Params { get; } = new();
    internal string SurfaceBody { get; set; } = "";
    internal int SurfaceLine { get; set; } = 1;
    internal DateTime LastWrite { get; set; }

    /// <summary>The compiled program, or null while the file has errors.</summary>
    public Shader? Program { get; internal set; }

    /// <summary>The last error (a parse error or the GLSL compiler's message), or null.</summary>
    public string? Error { get; internal set; }
}

/// <summary>
/// The GT67 shader language: a <c>.shader</c> file describes how a surface looks, in a few lines, and the engine
/// builds a full lit shader from it (lighting, shadows, fog, rain and puddles all keep working).
/// <code>
/// // Shaders/Custom/Pulse.shader
/// param color Tint = (1.0, 0.4, 0.1)
/// param float Speed = 2.0
///
/// surface
/// {
///     float pulse = 0.5 + 0.5 * sin(time * Speed);
///     albedo = base * Tint;
///     emission = Tint * pulse;
/// }
/// </code>
/// Inside <c>surface</c> you write GLSL. You can read <c>uv</c>, <c>position</c> (world), <c>normal</c>, <c>view</c>
/// (towards the camera), <c>time</c> (seconds) and <c>base</c> (the object's colour and texture), and you set
/// <c>albedo</c>, <c>emission</c> (glow), <c>shininess</c> and <c>specular</c> (a multiplier). Parameters can be
/// <c>float</c>, <c>color</c>, <c>vec2</c> or <c>vec3</c>; they show up in the editor and are saved with the object.
/// Files are looked up in <c>Shaders/Custom</c> (and in the shipped game's <c>Assets/Game/Shaders</c>) and reload
/// by themselves when you save them.
/// </summary>
public sealed class ShaderLibrary : IDisposable
{
    public const string Extension = ".shader";

    private readonly string _engineDirectory;
    private readonly List<string> _searchDirectories = new();
    private readonly Dictionary<string, CustomShader> _shaders = new(StringComparer.OrdinalIgnoreCase);
    private double _sinceScan = 10;

    public ShaderLibrary(string engineShaderDirectory)
    {
        _engineDirectory = engineShaderDirectory;
        AddDirectory(Path.Combine(engineShaderDirectory, "Custom"));
        AddDirectory(Path.Combine(AppContext.BaseDirectory, "Assets", "Game", "Shaders"));
    }

    public IReadOnlyCollection<CustomShader> Shaders => _shaders.Values;

    /// <summary>Looks in another folder for .shader files (the editor adds the project's Shaders/Custom, which comes first).</summary>
    public void AddDirectory(string directory, bool first = false)
    {
        if (_searchDirectories.Contains(directory, StringComparer.OrdinalIgnoreCase)) return;
        if (first) _searchDirectories.Insert(0, directory);
        else _searchDirectories.Add(directory);
        _sinceScan = 10;
    }

    /// <summary>Rebuilds every custom shader at the next scan (the engine's lit.frag they are made from changed).</summary>
    public void Invalidate()
    {
        foreach (var shader in _shaders.Values) shader.LastWrite = default;
        _sinceScan = 10;
    }

    public CustomShader? Get(string name) =>
        !string.IsNullOrEmpty(name) && _shaders.TryGetValue(name, out var shader) ? shader : null;

    /// <summary>Called every frame: looks for new, changed and removed files about twice a second and rebuilds what changed.</summary>
    public void Update(float deltaTime)
    {
        _sinceScan += deltaTime;
        if (_sinceScan < 0.5) return;
        _sinceScan = 0;
        Scan();
    }

    private void Scan()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in _searchDirectories)
        {
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*" + Extension))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!seen.Add(name)) continue;   // an earlier folder wins
                DateTime written = File.GetLastWriteTimeUtc(file);
                if (_shaders.TryGetValue(name, out var known) && known.File == file && known.LastWrite == written) continue;
                Build(name, file, written);
            }
        }

        foreach (var gone in _shaders.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _shaders[gone].Program?.Dispose();
            _shaders.Remove(gone);
        }
    }

    private void Build(string name, string file, DateTime written)
    {
        string text;
        try { text = System.IO.File.ReadAllText(file); }
        catch (IOException) { return; }   // still being saved: try again at the next scan

        var shader = Parse(name, file, text, out var error);
        shader.LastWrite = written;
        if (error == null)
        {
            try
            {
                string vert = System.IO.File.ReadAllText(Path.Combine(_engineDirectory, "lit.vert"));
                string frag = Generate(shader, System.IO.File.ReadAllText(Path.Combine(_engineDirectory, "lit.frag")));
                shader.Program = Shader.FromSource("custom:" + name, vert, frag);
                Log.Info($"Custom shader '{name}' compiled.");
            }
            catch (InvalidOperationException)
            {
                error = Shader.LastError ?? "The shader did not compile.";
            }
        }
        shader.Error = error;
        if (error != null) Log.Warn($"Custom shader '{name}': {error}");

        if (_shaders.TryGetValue(name, out var old))
        {
            // Keep working with the previous good version while the new one has errors
            if (shader.Program == null) { old.Error = shader.Error; old.LastWrite = written; return; }
            old.Program?.Dispose();
        }
        _shaders[name] = shader;
    }

    // ---- the language ----------------------------------------------------------------------------

    /// <summary>Reads a .shader file: <c>param</c> lines and one <c>surface { ... }</c> block.</summary>
    public static CustomShader Parse(string name, string file, string text, out string? error)
    {
        var shader = new CustomShader { Name = name, File = file };
        error = null;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        int surfaceStart = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            string code = (comment >= 0 ? line[..comment] : line).Trim();
            if (code.Length == 0) continue;

            if (code.StartsWith("param ", StringComparison.Ordinal))
            {
                if (!TryParseParam(code, out var param, out var problem))
                {
                    error = $"line {i + 1}: {problem}";
                    return shader;
                }
                shader.Params.Add(param!);
            }
            else if (code.StartsWith("surface", StringComparison.Ordinal))
            {
                surfaceStart = i;
                break;
            }
            else
            {
                error = $"line {i + 1}: expected 'param ...' or 'surface {{ ... }}', found '{code}'.";
                return shader;
            }
        }

        if (surfaceStart < 0)
        {
            error = "there is no 'surface { ... }' block.";
            return shader;
        }

        // Everything from the first { after 'surface' to its matching }
        var rest = new StringBuilder();
        for (int i = surfaceStart; i < lines.Length; i++) rest.Append(lines[i]).Append('\n');
        string body = rest.ToString();
        int open = body.IndexOf('{');
        if (open < 0) { error = $"line {surfaceStart + 1}: 'surface' needs a {{ ... }} block."; return shader; }

        int depth = 0, close = -1;
        for (int i = open; i < body.Length; i++)
        {
            if (body[i] == '{') depth++;
            else if (body[i] == '}' && --depth == 0) { close = i; break; }
        }
        if (close < 0) { error = $"line {surfaceStart + 1}: the surface block is never closed with }}."; return shader; }

        shader.SurfaceBody = body[(open + 1)..close];
        shader.SurfaceLine = surfaceStart + 1 + body[..open].Count(c => c == '\n');
        return shader;
    }

    private static bool TryParseParam(string code, out ShaderParam? param, out string problem)
    {
        param = null;
        problem = "";

        // param <type> <Name> = <value>
        int equals = code.IndexOf('=');
        var head = (equals < 0 ? code : code[..equals]).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (head.Length != 3) { problem = "write it as: param float Name = 1.0"; return false; }

        ShaderParamKind kind;
        switch (head[1])
        {
            case "float": kind = ShaderParamKind.Float; break;
            case "color": kind = ShaderParamKind.Color; break;
            case "vec2": kind = ShaderParamKind.Vec2; break;
            case "vec3": kind = ShaderParamKind.Vec3; break;
            default: problem = $"unknown type '{head[1]}' (use float, color, vec2 or vec3)."; return false;
        }

        string name = head[2];
        if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_') || !name.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            problem = $"'{name}' is not a valid name.";
            return false;
        }

        var values = new List<float>();
        if (equals >= 0)
        {
            string text = code[(equals + 1)..].Trim().Trim('(', ')');
            foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!float.TryParse(part.TrimEnd('f', 'F'), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    problem = $"'{part}' is not a number.";
                    return false;
                }
                values.Add(value);
            }
        }

        int count = kind switch { ShaderParamKind.Float => 1, ShaderParamKind.Vec2 => 2, _ => 3 };
        while (values.Count < count) values.Add(kind == ShaderParamKind.Color ? 1f : 0f);
        if (values.Count > count) { problem = $"{head[1]} takes {count} value(s)."; return false; }

        param = new ShaderParam
        {
            Name = name,
            Kind = kind,
            Default = new Vector4(values[0], count > 1 ? values[1] : 0f, count > 2 ? values[2] : 0f, 0f),
        };
        return true;
    }

    /// <summary>Builds the full fragment shader: the engine's lit.frag with this shader's parameters and surface function plugged in.</summary>
    public static string Generate(CustomShader shader, string litFragment)
    {
        var declarations = new StringBuilder();
        foreach (var param in shader.Params)
        {
            string type = param.Kind switch { ShaderParamKind.Float => "float", ShaderParamKind.Vec2 => "vec2", _ => "vec3" };
            declarations.Append("uniform ").Append(type).Append(' ').Append(param.Name).AppendLine(";");
        }
        declarations.AppendLine("void userSurface(inout vec3 albedo, inout vec3 emission, inout float shininess, inout float specular,");
        declarations.AppendLine("                 vec2 uv, vec3 position, vec3 normal, vec3 view, float time, vec3 base)");
        declarations.AppendLine("{");
        declarations.AppendLine("#line " + shader.SurfaceLine);
        declarations.AppendLine(shader.SurfaceBody);
        declarations.AppendLine("}");

        string call = "userSurface(albedo, userEmission, userShin, userSpec, fs.uv, fs.worldPos, n, v, uWeatherTime, base.rgb);";
        return litFragment
            .Replace("//@USER_DECLARATIONS@", declarations.ToString())
            .Replace("//@USER_CALL@", call);
    }

    /// <summary>The text of a new shader file.</summary>
    public static string Template(string name) => $$"""
        // {{name}}: a custom surface shader. Save the file and the engine reloads it by itself.
        // 'param' lines become values you can change per object in the editor's Inspector.
        param color Tint = (1.0, 0.45, 0.15)
        param float Speed = 2.0
        param float Glow = 1.0

        // Inside 'surface' you write GLSL. You can read:
        //   uv, position (world), normal, view (towards the camera), time (seconds), base (the object's colour)
        // and set:
        //   albedo (colour), emission (glow, added on top), shininess, specular (a multiplier)
        surface
        {
            float pulse = 0.5 + 0.5 * sin(time * Speed + position.y * 2.0);
            albedo = base * Tint;
            emission = Tint * pulse * Glow;
        }
        """;

    public void Dispose()
    {
        foreach (var shader in _shaders.Values) shader.Program?.Dispose();
        _shaders.Clear();
    }
}
