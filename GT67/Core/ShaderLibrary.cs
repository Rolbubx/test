using System.Globalization;
using System.Text;
using GT67.Utils;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>What kind of value a shader parameter holds.</summary>
public enum ShaderParamKind { Float, Color, Vec2, Vec3, Bool, Texture }

/// <summary>A <c>param</c> line of a .shader file: a named value you can change per object.</summary>
public sealed class ShaderParam
{
    public string Name { get; init; } = "";
    public ShaderParamKind Kind { get; init; }
    public Vector4 Default { get; init; }

    /// <summary>Slider limits from <c>range min max</c> (floats only), or null for a free value.</summary>
    public Vector2? Range { get; init; }

    /// <summary>The default image of a <c>texture</c> parameter (a path relative to the executable), or empty for plain white.</summary>
    public string DefaultTexture { get; init; } = "";
}

/// <summary>A shader written in the small GT67 shader language (a <c>.shader</c> file), compiled into the lit shader.</summary>
public sealed class CustomShader
{
    public string Name { get; init; } = "";
    public string File { get; init; } = "";
    public List<ShaderParam> Params { get; } = new();
    internal string SurfaceBody { get; set; } = "";
    internal int SurfaceLine { get; set; } = 1;
    internal string? VertexBody { get; set; }
    internal int VertexLine { get; set; } = 1;
    internal string? ScreenBody { get; set; }
    internal int ScreenLine { get; set; } = 1;
    internal string? SkyBody { get; set; }
    internal int SkyLine { get; set; } = 1;

    /// <summary>
    /// True for a sky shader (a <c>sky { ... }</c> block): it changes or replaces the sky (and can draw its own sun)
    /// and is picked for a scene's sky in Environment (<see cref="Skybox.CustomShader"/>).
    /// </summary>
    public bool IsSky => SkyBody != null;
    internal DateTime LastWrite { get; set; }

    /// <summary>
    /// True for a screen shader (a <c>screen { ... }</c> block): it changes the whole picture, not one object, and is
    /// added to a scene's Volume (<see cref="PostProfile.AddScreenShader"/>) instead of to a material.
    /// </summary>
    public bool IsScreen => ScreenBody != null;

    /// <summary>Screen shaders: before tone mapping on linear HDR colour (<c>stage hdr</c>) or after it on display colour (the default).</summary>
    public PostStage Stage { get; internal set; } = PostStage.Ldr;

    /// <summary>
    /// Screen shaders: <c>vertexSnap N</c> rounds every vertex of the scene to a grid N rows high on the screen while
    /// the shader is on, so geometry wobbles like on the first PlayStation (0 = off).
    /// </summary>
    public float VertexSnap { get; internal set; }

    /// <summary>The compiled program, or null while the file has errors.</summary>
    public Shader? Program { get; internal set; }

    /// <summary>The last error (a parse error or the GLSL compiler's message), or null.</summary>
    public string? Error { get; internal set; }
}

/// <summary>
/// The GT67 shader language: a <c>.shader</c> file describes how a surface looks (and, optionally, how its vertices
/// move) in a few lines, and the engine builds a full lit shader from it (lighting, shadows, fog, rain, puddles and
/// caustics all keep working).
/// <code>
/// // Shaders/Custom/Pulse.shader
/// param color Tint = (1.0, 0.4, 0.1)
/// param float Speed = 2.0 range 0 10        // a slider from 0 to 10 in the Inspector
/// param bool Animate = true                 // a checkbox
/// param texture Mask = "Assets/Textures/mask.png"
///
/// vertex                                    // optional: move the vertices (world space)
/// {
///     position += normal * sin(time * Speed + position.y * 4.0) * 0.03;
/// }
///
/// surface
/// {
///     float pulse = Animate ? 0.5 + 0.5 * sin(time * Speed) : 1.0;
///     albedo = base * Tint;
///     emission = Tint * pulse * texture(Mask, uv).r;
/// }
/// </code>
/// <para><b>surface</b> reads <c>uv</c>, <c>position</c> (world), <c>localPosition</c> (the model's own coordinates),
/// <c>objectPosition</c> (the object's origin), <c>view</c> (towards the camera), <c>time</c>, <c>base</c> (the
/// object's colour and texture), <c>vertexColor</c>, <c>screenUV</c> (0..1 across the screen),
/// <c>sunDirection</c>, <c>sunColor</c> and <c>wetness</c> (0..1 while it rains); it sets <c>albedo</c>,
/// <c>emission</c> (glow), <c>shininess</c>, <c>specular</c> (a multiplier), <c>normal</c> (bend it for bumps) and
/// <c>alpha</c> (below 0.5 the pixel is cut away: holes, dissolve, foliage).</para>
/// <para><b>vertex</b> reads and changes <c>position</c> (world), <c>normal</c> and <c>uv</c>, and reads
/// <c>localPosition</c>, <c>objectPosition</c>, <c>time</c> and <c>vertexColor</c>. Shadows and reflections still
/// use the unmoved shape.</para>
/// <para>Every shader also gets a small library: <c>hash</c>, <c>noise</c>, <c>fbm</c>, <c>voronoi</c>,
/// <c>remap</c>, <c>saturate</c>, <c>rotate</c>, <c>hsv</c>, <c>rgbToHsv</c>, <c>fresnel</c>, <c>stripes</c>,
/// <c>checker</c>, <c>circle</c>, <c>triplanar</c> and (in surface) <c>bump</c>; see <see cref="Library"/>.</para>
/// <para>Parameters can be <c>float</c> (optionally <c>range min max</c>), <c>color</c>, <c>vec2</c>, <c>vec3</c>,
/// <c>bool</c> or <c>texture</c>; they show up in the editor's Inspector and are saved with the object. Files are
/// looked up in <c>Shaders/Custom</c> (and in the shipped game's <c>Assets/Game/Shaders</c>) and reload by
/// themselves when you save them.</para>
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

    /// <summary>Rebuilds every custom shader at the next scan (the engine's lit shaders they are made from changed).</summary>
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
                if (shader.IsSky)
                {
                    string skyVert = System.IO.File.ReadAllText(Path.Combine(_engineDirectory, "sky.vert"));
                    string skyFrag = GenerateSky(shader, System.IO.File.ReadAllText(Path.Combine(_engineDirectory, "sky.frag")));
                    shader.Program = Shader.FromSource("sky:" + name, skyVert, skyFrag);
                    Log.Info($"Sky shader '{name}' compiled.");
                }
                else if (shader.IsScreen)
                {
                    string postVert = System.IO.File.ReadAllText(Path.Combine(_engineDirectory, "post.vert"));
                    shader.Program = Shader.FromSource("screen:" + name, postVert, GenerateScreen(shader));
                    Log.Info($"Screen shader '{name}' compiled.");
                }
                else
                {
                    string vert = GenerateVertex(shader, System.IO.File.ReadAllText(Path.Combine(_engineDirectory, "lit.vert")));
                    string frag = Generate(shader, System.IO.File.ReadAllText(Path.Combine(_engineDirectory, "lit.frag")));
                    shader.Program = Shader.FromSource("custom:" + name, vert, frag);
                    Log.Info($"Custom shader '{name}' compiled.");
                }
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

    /// <summary>Reads a .shader file: <c>param</c> lines, then a <c>vertex { ... }</c> and/or a <c>surface { ... }</c> block.</summary>
    public static CustomShader Parse(string name, string file, string text, out string? error)
    {
        var shader = new CustomShader { Name = name, File = file };
        error = null;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        bool hasSurface = false;
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
                if (shader.Params.Any(p => p.Name == param!.Name))
                {
                    error = $"line {i + 1}: there is already a parameter called '{param!.Name}'.";
                    return shader;
                }
                shader.Params.Add(param!);
                continue;
            }

            // Screen shaders: when they run, and the PS1-style vertex snapping
            var words = code.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words[0] == "stage")
            {
                if (words.Length != 2 || words[1] is not ("hdr" or "ldr"))
                {
                    error = $"line {i + 1}: write 'stage ldr' (after tone mapping, the default) or 'stage hdr' (before it).";
                    return shader;
                }
                shader.Stage = words[1] == "hdr" ? PostStage.Hdr : PostStage.Ldr;
                continue;
            }
            if (words[0] == "vertexSnap")
            {
                if (words.Length != 2 || !TryNumber(words[1], out float snap) || snap < 0f)
                {
                    error = $"line {i + 1}: write 'vertexSnap 120' (the height of the grid vertices snap to; 0 = off).";
                    return shader;
                }
                shader.VertexSnap = snap;
                continue;
            }

            string? block = IsBlock(code, "surface") ? "surface" : IsBlock(code, "vertex") ? "vertex"
                : IsBlock(code, "screen") ? "screen" : IsBlock(code, "sky") ? "sky" : null;
            if (block == null)
            {
                error = $"line {i + 1}: expected 'param ...', 'vertex {{ ... }}', 'surface {{ ... }}', 'screen {{ ... }}' or 'sky {{ ... }}', found '{code}'.";
                return shader;
            }
            if ((block == "surface" && hasSurface) || (block == "vertex" && shader.VertexBody != null) ||
                (block == "screen" && shader.ScreenBody != null) || (block == "sky" && shader.SkyBody != null))
            {
                error = $"line {i + 1}: there is already a '{block}' block.";
                return shader;
            }
            if (!TryReadBlock(lines, i, out string body, out int bodyLine, out int lastLine, out var problemText))
            {
                error = $"line {i + 1}: {problemText}";
                return shader;
            }
            if (block == "surface")
            {
                shader.SurfaceBody = body;
                shader.SurfaceLine = bodyLine;
                hasSurface = true;
            }
            else if (block == "screen")
            {
                shader.ScreenBody = body;
                shader.ScreenLine = bodyLine;
            }
            else if (block == "sky")
            {
                shader.SkyBody = body;
                shader.SkyLine = bodyLine;
            }
            else
            {
                shader.VertexBody = body;
                shader.VertexLine = bodyLine;
            }
            i = lastLine;
        }

        int kinds = (hasSurface || shader.VertexBody != null ? 1 : 0) + (shader.ScreenBody != null ? 1 : 0) + (shader.SkyBody != null ? 1 : 0);
        if (kinds > 1)
            error = "a file is one kind of shader: an object ('surface' / 'vertex'), the whole picture ('screen') or the sky ('sky').";
        else if (kinds == 0)
            error = "there is no 'surface { ... }', 'vertex { ... }', 'screen { ... }' or 'sky { ... }' block.";
        return shader;
    }

    private static bool IsBlock(string code, string keyword) =>
        code.StartsWith(keyword, StringComparison.Ordinal) && (code.Length == keyword.Length || code[keyword.Length] is ' ' or '{' or '\t');

    /// <summary>The text between the first { from line <paramref name="start"/> and its matching } (braces in // comments are ignored).</summary>
    private static bool TryReadBlock(string[] lines, int start, out string body, out int bodyLine, out int lastLine, out string problem)
    {
        body = "";
        bodyLine = start + 1;
        lastLine = start;
        problem = "";
        var text = new StringBuilder();
        int depth = 0;
        bool opened = false;
        for (int i = start; i < lines.Length; i++)
        {
            string line = lines[i];
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            int limit = comment >= 0 ? comment : line.Length;
            for (int c = 0; c < line.Length; c++)
            {
                char ch = line[c];
                if (c < limit && ch == '{')
                {
                    if (depth++ == 0)
                    {
                        opened = true;
                        bodyLine = i + 1;   // the block's first line is the one after (or on) the brace
                        continue;
                    }
                }
                else if (c < limit && ch == '}' && opened && --depth == 0)
                {
                    body = text.ToString();
                    lastLine = i;
                    return true;
                }
                if (opened) text.Append(ch);
            }
            if (opened) text.Append('\n');
        }
        problem = opened ? "the block is never closed with }." : "the block needs { ... }.";
        return false;
    }

    private static bool TryParseParam(string code, out ShaderParam? param, out string problem)
    {
        param = null;
        problem = "";

        // param <type> <Name> = <value> [range <min> <max>]
        Vector2? range = null;
        int rangeAt = code.IndexOf(" range ", StringComparison.Ordinal);
        if (rangeAt >= 0)
        {
            var limits = code[(rangeAt + 7)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (limits.Length != 2 || !TryNumber(limits[0], out float min) || !TryNumber(limits[1], out float max) || max <= min)
            {
                problem = "write the range as: range 0 10 (smallest, then largest).";
                return false;
            }
            range = new Vector2(min, max);
            code = code[..rangeAt];
        }

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
            case "bool": kind = ShaderParamKind.Bool; break;
            case "texture": kind = ShaderParamKind.Texture; break;
            default: problem = $"unknown type '{head[1]}' (use float, color, vec2, vec3, bool or texture)."; return false;
        }
        if (range != null && kind != ShaderParamKind.Float) { problem = "only float parameters can have a range."; return false; }

        string name = head[2];
        if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_') || !name.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            problem = $"'{name}' is not a valid name.";
            return false;
        }
        if (ReservedNames.Contains(name)) { problem = $"'{name}' is already used by the engine; pick another name."; return false; }

        string value = equals >= 0 ? code[(equals + 1)..].Trim() : "";

        if (kind == ShaderParamKind.Texture)
        {
            param = new ShaderParam { Name = name, Kind = kind, DefaultTexture = value.Trim('"', '\'') };
            return true;
        }
        if (kind == ShaderParamKind.Bool)
        {
            bool on = value.Length == 0 || value is "true" or "1" or "on";
            if (value.Length > 0 && value is not ("true" or "false" or "1" or "0" or "on" or "off"))
            {
                problem = $"'{value}' is not true or false.";
                return false;
            }
            param = new ShaderParam { Name = name, Kind = kind, Default = new Vector4(on ? 1f : 0f, 0f, 0f, 0f) };
            return true;
        }

        var values = new List<float>();
        foreach (var part in value.Trim('(', ')').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryNumber(part, out float number))
            {
                problem = $"'{part}' is not a number.";
                return false;
            }
            values.Add(number);
        }

        int count = kind switch { ShaderParamKind.Float => 1, ShaderParamKind.Vec2 => 2, _ => 3 };
        while (values.Count < count) values.Add(kind == ShaderParamKind.Color ? 1f : 0f);
        if (values.Count > count) { problem = $"{head[1]} takes {count} value(s)."; return false; }

        param = new ShaderParam
        {
            Name = name,
            Kind = kind,
            Range = range,
            Default = new Vector4(values[0], count > 1 ? values[1] : 0f, count > 2 ? values[2] : 0f, 0f),
        };
        return true;
    }

    private static bool TryNumber(string text, out float value) =>
        float.TryParse(text.TrimEnd('f', 'F'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>Names a parameter cannot take: the inputs and outputs of the blocks and the library's functions.</summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "albedo", "emission", "shininess", "specular", "normal", "alpha", "uv", "position", "localPosition",
        "objectPosition", "view", "time", "base", "vertexColor", "screenUV", "sunDirection", "sunColor", "wetness",
        "hash", "hash2", "noise", "fbm", "voronoi", "remap", "saturate", "rotate", "hsv", "rgbToHsv", "fresnel",
        "stripes", "checker", "circle", "triplanar", "bump",
        "color", "resolution", "depth", "sampleScene", "sampleDepth", "dither", "worldPosition", "cameraPosition",
        "sampleWorld", "isSky", "direction", "sunSize", "sunDisc", "skyColor",
    };

    // ---- generated GLSL ----------------------------------------------------------------------------

    /// <summary>
    /// The functions every custom shader can call (vertex and surface). All noise is 0..1 and smooth; p can be any
    /// world or uv coordinate (scale it to set the size of the pattern).
    /// </summary>
    public const string Library = """
        // ---- GT67 shader library ----
        // hash(p): a stable random number 0..1 for any point (vec2 or vec3). hash2(p): two of them.
        float hash(vec2 p) { p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }
        float hash(vec3 p) { p = fract(p * vec3(0.1031, 0.1030, 0.0973)); p += dot(p, p.yxz + 33.33); return fract((p.x + p.y) * p.z); }
        vec2 hash2(vec2 p) { return fract(sin(vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)))) * 43758.5453); }

        // noise(p): smooth gradient (Perlin-style) noise, 0..1.
        float noise(vec2 p)
        {
            vec2 i = floor(p), f = fract(p);
            vec2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
            float a = dot(hash2(i) * 2.0 - 1.0, f);
            float b = dot(hash2(i + vec2(1.0, 0.0)) * 2.0 - 1.0, f - vec2(1.0, 0.0));
            float c = dot(hash2(i + vec2(0.0, 1.0)) * 2.0 - 1.0, f - vec2(0.0, 1.0));
            float d = dot(hash2(i + vec2(1.0, 1.0)) * 2.0 - 1.0, f - vec2(1.0, 1.0));
            return mix(mix(a, b, u.x), mix(c, d, u.x), u.y) * 0.7 + 0.5;
        }
        float noise(vec3 p)
        {
            vec3 i = floor(p), f = fract(p);
            vec3 u = f * f * (3.0 - 2.0 * f);
            return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), u.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), u.x), u.y),
                       mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), u.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), u.x), u.y), u.z);
        }

        // fbm(p, octaves): noise layered at doubling detail (clouds, rock, dirt), 0..1. octaves 1..8.
        float fbm(vec2 p, int octaves)
        {
            float sum = 0.0, amplitude = 0.5, total = 0.0;
            for (int i = 0; i < 8; ++i) { if (i >= octaves) break; sum += amplitude * noise(p); total += amplitude; p = p * 2.03 + 17.1; amplitude *= 0.5; }
            return sum / max(total, 1e-4);
        }
        float fbm(vec3 p, int octaves)
        {
            float sum = 0.0, amplitude = 0.5, total = 0.0;
            for (int i = 0; i < 8; ++i) { if (i >= octaves) break; sum += amplitude * noise(p); total += amplitude; p = p * 2.03 + 17.1; amplitude *= 0.5; }
            return sum / max(total, 1e-4);
        }

        // voronoi(p): x = distance to the nearest cell centre (0 at a centre), y = a random id 0..1 for that cell,
        // z = distance to the cell's edge (0 on the edge: cracks, tiles, scales).
        vec3 voronoi(vec2 p)
        {
            vec2 cell = floor(p), f = fract(p);
            float d1 = 8.0, d2 = 8.0, id = 0.0;
            for (int j = -1; j <= 1; ++j)
            for (int i = -1; i <= 1; ++i)
            {
                vec2 g = vec2(float(i), float(j));
                vec2 r = g + hash2(cell + g) - f;
                float d = dot(r, r);
                if (d < d1) { d2 = d1; d1 = d; id = hash(cell + g); } else if (d < d2) { d2 = d; }
            }
            return vec3(sqrt(d1), id, sqrt(d2) - sqrt(d1));
        }

        // remap(x, a, b, c, d): x from the range a..b into c..d (not clamped). saturate(x): clamp to 0..1.
        float remap(float x, float a, float b, float c, float d) { return c + (x - a) * (d - c) / (b - a); }
        float saturate(float x) { return clamp(x, 0.0, 1.0); }
        vec3 saturate(vec3 x) { return clamp(x, 0.0, 1.0); }

        // rotate(p, angle): turns a 2D point around (0, 0) by angle radians.
        vec2 rotate(vec2 p, float angle) { float c = cos(angle), s = sin(angle); return vec2(c * p.x - s * p.y, s * p.x + c * p.y); }

        // hsv(vec3(hue 0..1, saturation, value)) -> rgb, and back.
        vec3 hsv(vec3 c) { vec3 p = abs(fract(c.xxx + vec3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0); return c.z * mix(vec3(1.0), clamp(p - 1.0, 0.0, 1.0), c.y); }
        vec3 rgbToHsv(vec3 c)
        {
            vec4 k = vec4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
            vec4 p = mix(vec4(c.bg, k.wz), vec4(c.gb, k.xy), step(c.b, c.g));
            vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));
            float d = q.x - min(q.w, q.y);
            return vec3(abs(q.z + (q.w - q.y) / (6.0 * d + 1e-10)), d / (q.x + 1e-10), q.x);
        }

        // fresnel(normal, view, power): 0 facing the camera, 1 at the silhouette (rims, glass, force fields).
        float fresnel(vec3 n, vec3 v, float power) { return pow(1.0 - clamp(dot(normalize(n), normalize(v)), 0.0, 1.0), power); }

        // Patterns, 0..1: stripes(x, count) soft bands; checker(uv, count) squares; circle(uv, centre, radius) a soft disc.
        float stripes(float x, float count) { return 0.5 + 0.5 * sin(x * count * 6.2831853); }
        float checker(vec2 uv, float count) { vec2 c = floor(uv * count); return mod(c.x + c.y, 2.0); }
        float circle(vec2 uv, vec2 centre, float radius) { float d = length(uv - centre); return 1.0 - smoothstep(radius - fwidthSafe(d), radius + fwidthSafe(d), d); }

        // triplanar(texture, position, normal, scale): a texture projected from the three axes, without uvs
        // (terrain, rocks, anything without good uvs).
        vec4 triplanar(sampler2D tex, vec3 p, vec3 n, float scale)
        {
            vec3 w = pow(abs(normalize(n)), vec3(4.0));
            w /= (w.x + w.y + w.z);
            return texture(tex, p.zy * scale) * w.x + texture(tex, p.xz * scale) * w.y + texture(tex, p.xy * scale) * w.z;
        }
        """;

    /// <summary>The library parts that only exist in the surface stage (they need screen-space derivatives).</summary>
    private const string SurfaceLibrary = """
        float fwidthSafe(float x) { return fwidth(x); }

        // bump(normal, position, height, strength): bends the normal as if the surface had the given height
        // (from noise, a texture...): normal = bump(normal, position, fbm(position.xz * 4.0, 4), 0.3);
        vec3 bump(vec3 n, vec3 p, float height, float strength)
        {
            vec3 dpdx = dFdx(p), dpdy = dFdy(p);
            float dhdx = dFdx(height), dhdy = dFdy(height);
            vec3 r1 = cross(dpdy, n), r2 = cross(n, dpdx);
            float det = dot(dpdx, r1);
            vec3 grad = sign(det) * (dhdx * r1 + dhdy * r2);
            return normalize(abs(det) * n - grad * strength);
        }
        """;

    private static string Uniforms(CustomShader shader)
    {
        var declarations = new StringBuilder();
        foreach (var param in shader.Params)
        {
            string type = param.Kind switch
            {
                ShaderParamKind.Float => "float",
                ShaderParamKind.Vec2 => "vec2",
                ShaderParamKind.Bool => "bool",
                ShaderParamKind.Texture => "sampler2D",
                _ => "vec3",
            };
            declarations.Append("uniform ").Append(type).Append(' ').Append(param.Name).AppendLine(";");
        }
        return declarations.ToString();
    }

    /// <summary>Builds the full fragment shader: the engine's lit.frag with this shader's parameters and surface function plugged in.</summary>
    public static string Generate(CustomShader shader, string litFragment)
    {
        var declarations = new StringBuilder();
        declarations.Append(Uniforms(shader));
        declarations.AppendLine(SurfaceLibrary);
        declarations.AppendLine(Library);
        declarations.AppendLine("void userSurface(inout vec3 albedo, inout vec3 emission, inout float shininess, inout float specular,");
        declarations.AppendLine("                 inout vec3 normal, inout float alpha, vec2 uv, vec3 position, vec3 localPosition,");
        declarations.AppendLine("                 vec3 objectPosition, vec3 view, float time, vec3 base, vec4 vertexColor, vec2 screenUV,");
        declarations.AppendLine("                 vec3 sunDirection, vec3 sunColor, float wetness)");
        declarations.AppendLine("{");
        declarations.AppendLine("#line " + shader.SurfaceLine);
        declarations.AppendLine(shader.SurfaceBody);
        declarations.AppendLine("}");

        const string call =
            "userSurface(albedo, userEmission, userShin, userSpec, n, userAlpha, fs.uv, fs.worldPos, fs.localPos, fs.objectPos, v, " +
            "uWeatherTime, base.rgb, fs.color, gl_FragCoord.xy / max(uScreenSize, vec2(1.0)), " +
            "uHasDirLight ? normalize(-uDirLight.direction) : vec3(0.0, 1.0, 0.0), uHasDirLight ? uDirLight.color : vec3(0.0), uWetness);\n" +
            "    if (userAlpha < 0.5) discard;\n" +
            "    n = normalize(n);";
        return litFragment
            .Replace("//@USER_DECLARATIONS@", declarations.ToString())
            .Replace("//@USER_CALL@", call);
    }

    /// <summary>Builds the vertex shader: the engine's lit.vert, with this shader's vertex function plugged in (if it has one).</summary>
    public static string GenerateVertex(CustomShader shader, string litVertex)
    {
        if (shader.VertexBody == null) return litVertex;

        var declarations = new StringBuilder();
        declarations.AppendLine("uniform float uWeatherTime;");
        declarations.Append(Uniforms(shader));
        declarations.AppendLine("float fwidthSafe(float x) { return 0.0; }");   // no derivatives in the vertex stage
        declarations.AppendLine(Library);
        declarations.AppendLine("void userVertex(inout vec3 position, inout vec3 normal, inout vec2 uv, vec3 localPosition,");
        declarations.AppendLine("                vec3 objectPosition, float time, vec4 vertexColor)");
        declarations.AppendLine("{");
        declarations.AppendLine("#line " + shader.VertexLine);
        declarations.AppendLine(shader.VertexBody);
        declarations.AppendLine("}");

        const string call = "userVertex(worldPos, worldNormal, uv, aPosition, objectPos, uWeatherTime, aColor);";
        return litVertex
            .Replace("//@USER_VERTEX_DECLARATIONS@", declarations.ToString())
            .Replace("//@USER_VERTEX_CALL@", call);
    }

    /// <summary>
    /// Builds the fragment shader of a screen shader: it runs once for every pixel of the picture, with the scene's
    /// colour there in <c>color</c> (read it and change it), plus helpers to read the picture and the depth elsewhere.
    /// </summary>
    public static string GenerateScreen(CustomShader shader)
    {
        var s = new StringBuilder();
        s.AppendLine("in vec2 vUV;");
        s.AppendLine("out vec4 FragColor;");
        s.AppendLine("uniform sampler2D uInput;");
        s.AppendLine("uniform sampler2D uDepthTexture;");
        s.AppendLine("uniform vec2 uResolution;");
        s.AppendLine("uniform float uTime;");
        s.AppendLine("uniform float uNear;");
        s.AppendLine("uniform float uFar;");
        s.AppendLine("uniform mat4 uInvViewProjection;");
        s.AppendLine("uniform vec3 uCameraPosition;");
        s.AppendLine("uniform vec3 uSunDirection;");
        s.AppendLine("uniform vec3 uSunColor;");
        s.Append(Uniforms(shader));
        s.AppendLine("float fwidthSafe(float x) { return fwidth(x); }");
        s.AppendLine(Library);
        s.AppendLine(ScreenLibrary);
        s.AppendLine("void userScreen(inout vec3 color, vec2 uv, vec2 resolution, float time, float depth, vec3 worldPosition,");
        s.AppendLine("                vec3 cameraPosition, vec3 sunDirection, vec3 sunColor)");
        s.AppendLine("{");
        s.AppendLine("#line " + shader.ScreenLine);
        s.AppendLine(shader.ScreenBody);
        s.AppendLine("}");
        s.AppendLine("void main()");
        s.AppendLine("{");
        s.AppendLine("    vec3 color = texture(uInput, vUV).rgb;");
        s.AppendLine("    userScreen(color, vUV, uResolution, uTime, sampleDepth(vUV), sampleWorld(vUV), uCameraPosition, uSunDirection, uSunColor);");
        s.AppendLine("    FragColor = vec4(color, 1.0);");
        s.AppendLine("}");
        return s.ToString();
    }

    /// <summary>
    /// Builds a sky shader: the engine's sky.frag with this shader's function run for every direction of the sky, after
    /// the engine's own sky (atmosphere with clouds, cubemap or gradient) and before the storm and the fog.
    /// </summary>
    public static string GenerateSky(CustomShader shader, string skyFragment)
    {
        var s = new StringBuilder();
        s.AppendLine("uniform vec3 uUserSunDirection;");
        s.AppendLine("uniform vec3 uUserSunColor;");
        s.AppendLine("uniform float uUserSunSize;");
        s.AppendLine("uniform float uUserTime;");
        s.AppendLine("uniform vec3 uUserCameraPosition;");
        s.Append(Uniforms(shader));
        s.AppendLine("float fwidthSafe(float x) { return fwidth(x); }");
        s.AppendLine(Library);
        s.AppendLine("""
            // sunDisc(direction, sunDirection, size): 1 inside a disc of 'size' radians around the sun, 0 outside, with a smooth edge.
            float sunDisc(vec3 d, vec3 s, float size)
            {
                float c = dot(normalize(d), normalize(s));
                float edge = max(fwidth(c), 1e-5);
                return smoothstep(cos(size) - edge, cos(size) + edge, c);
            }
            // skyColor(direction): the engine's own sky in any direction (atmosphere with clouds, cubemap or gradient).
            vec3 skyColor(vec3 d) { return baseSky(normalize(d)); }
            """);
        s.AppendLine("void userSky(inout vec3 color, vec3 direction, vec3 sunDirection, vec3 sunColor, float sunSize, float time,");
        s.AppendLine("             vec3 cameraPosition)");
        s.AppendLine("{");
        s.AppendLine("#line " + shader.SkyLine);
        s.AppendLine(shader.SkyBody);
        s.AppendLine("}");

        const string call = "userSky(color, dir, uUserSunDirection, uUserSunColor, uUserSunSize, uUserTime, uUserCameraPosition);";
        return skyFragment
            .Replace("//@USER_SKY_DECLARATIONS@", s.ToString())
            .Replace("//@USER_SKY_CALL@", call);
    }

    /// <summary>The text of a new sky shader file.</summary>
    public static string SkyTemplate(string name) => $$"""
        // {{name}}: a sky shader. Pick it for a scene's sky in the Hierarchy > Environment > Sky. Save the file and
        // the engine reloads it by itself. Colours here are linear light (1 = white, the sun is much brighter).
        param color Zenith = (0.12, 0.3, 0.85)
        param color Horizon = (0.95, 0.7, 0.45)
        param float Blend = 1.0 range 0 1          // 0 = keep the engine's sky, 1 = only this one
        param float SunGlow = 3.0 range 0 20

        // 'color' starts as the engine's sky in this direction (physical atmosphere with clouds, or the gradient):
        // keep it, change it or replace it. You can read direction (unit vector), sunDirection (towards the sun),
        // sunColor (its light, zero when the sun is off), sunSize (radius in radians), time and cameraPosition, and call
        // sunDisc(direction, sunDirection, size), skyColor(direction) and the library (noise, fbm, voronoi, hash, hsv...).
        sky
        {
            float up = saturate(direction.y);
            vec3 mine = mix(Horizon, Zenith, pow(up, 0.45)) * 1.5;
            mine += sunColor * sunDisc(direction, sunDirection, sunSize) * 15.0;                  // the disc
            mine += sunColor * pow(saturate(dot(direction, sunDirection)), 48.0) * SunGlow * 0.1; // its glow
            color = mix(color, mine, Blend);
        }
        """;

    /// <summary>Helpers that only screen shaders have.</summary>
    private const string ScreenLibrary = """
        // sampleScene(uv): the picture's colour anywhere on the screen (uv 0..1). sampleDepth(uv): the distance in
        // metres from the camera to what is seen there (the far plane for the sky).
        vec3 sampleScene(vec2 p) { return texture(uInput, clamp(p, vec2(0.0), vec2(1.0))).rgb; }
        float sampleDepth(vec2 p)
        {
            float d = texture(uDepthTexture, clamp(p, vec2(0.0), vec2(1.0))).r;
            if (d >= 1.0) return uFar;
            float z = d * 2.0 - 1.0;
            return 2.0 * uNear * uFar / (uFar + uNear - z * (uFar - uNear));
        }

        // sampleWorld(uv): the world position (x, y, z in metres) of what is seen there; for the sky, a point on the far plane.
        vec3 sampleWorld(vec2 p)
        {
            float d = texture(uDepthTexture, clamp(p, vec2(0.0), vec2(1.0))).r;
            vec4 world = uInvViewProjection * vec4(p * 2.0 - 1.0, d * 2.0 - 1.0, 1.0);
            return world.xyz / world.w;
        }

        // isSky(uv): true where nothing was drawn (the sky).
        bool isSky(vec2 p) { return texture(uDepthTexture, clamp(p, vec2(0.0), vec2(1.0))).r >= 1.0; }

        // dither(pixel): the 4x4 ordered (Bayer) dither pattern, -0.5..0.5, for a whole pixel position (uv * resolution).
        float dither(vec2 pixel)
        {
            int x = int(mod(pixel.x, 4.0)), y = int(mod(pixel.y, 4.0));
            int index = x + y * 4;
            int bayer[16] = int[16](0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
            return (float(bayer[index]) + 0.5) / 16.0 - 0.5;
        }
        """;

    /// <summary>The text of a new screen shader file.</summary>
    public static string ScreenTemplate(string name) => $$"""
        // {{name}}: a screen shader. It changes the whole picture of the game (add it to a scene's Volume in the
        // editor's Hierarchy). Save the file and the engine reloads it by itself.
        // 'param' lines become settings in the Volume: float (add 'range 0 5' for a slider), color, vec2, vec3, bool, texture.
        param float Strength = 1.0 range 0 1
        param color Tint = (1.0, 0.9, 0.75)
        param float Lines = 240.0 range 20 1000

        // Optional: 'stage hdr' runs before tone mapping (on linear light) instead of after it.
        // Optional: 'vertexSnap 120' makes every vertex snap to a coarse grid, like the first PlayStation.

        // The picture: read and change 'color' (this pixel). You can read uv (0..1), resolution (pixels), time,
        // depth (metres from the camera to what is seen here), worldPosition (where that is: .y is its height) and
        // cameraPosition, and call sampleScene(uv), sampleDepth(uv), sampleWorld(uv), isSky(uv) and dither(pixel), plus
        // the library: noise, fbm, voronoi, hash, remap, saturate, rotate, hsv, rgbToHsv, stripes, checker, circle.
        screen
        {
            vec3 look = color * Tint * (0.85 + 0.15 * stripes(uv.y, Lines));
            color = mix(color, look, Strength);
        }
        """;

    /// <summary>The text of a new shader file.</summary>
    public static string Template(string name) => $$"""
        // {{name}}: a custom shader. Save the file and the engine reloads it by itself.
        // 'param' lines become values you can change per object in the editor's Inspector:
        //   float (add 'range 0 5' for a slider), color, vec2, vec3, bool (a checkbox), texture ("path/to/image.png")
        param color Tint = (1.0, 0.45, 0.15)
        param float Speed = 2.0 range 0 10
        param float Glow = 1.0 range 0 5
        param bool Sparkle = true

        // Optional: move the vertices. You can change position (world), normal and uv;
        // you can read localPosition, objectPosition, time and vertexColor.
        vertex
        {
            position += normal * sin(time * Speed + position.y * 3.0) * 0.02;
        }

        // The look. You can read uv, position, localPosition, objectPosition, view, time, base (the object's colour),
        // vertexColor, screenUV, sunDirection, sunColor and wetness, and set albedo, emission (glow), shininess,
        // specular, normal (for bumps) and alpha (below 0.5 the pixel is cut away).
        // Library: noise, fbm, voronoi, hash, remap, saturate, rotate, hsv, rgbToHsv, fresnel, stripes, checker,
        // circle, triplanar, bump.
        surface
        {
            float pulse = 0.5 + 0.5 * sin(time * Speed + position.y * 2.0);
            float grain = fbm(position * 3.0, 4);
            albedo = base * Tint * (0.8 + 0.4 * grain);
            emission = Tint * pulse * Glow;
            if (Sparkle) emission += vec3(1.0) * step(0.985, noise(position * 40.0 + time));
            emission += Tint * fresnel(normal, view, 3.0) * Glow;
        }
        """;

    public void Dispose()
    {
        foreach (var shader in _shaders.Values) shader.Program?.Dispose();
        _shaders.Clear();
    }
}
