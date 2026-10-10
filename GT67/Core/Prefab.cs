using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using GT67.Utils;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>
/// A reusable object saved to a file: <c>Prefabs/&lt;Name&gt;.prefab</c> (JSON) holds an object with its children,
/// mesh, material, texture, collision, tag and scripts (with their settings). Spawn copies of it anywhere:
/// <c>Prefab.Spawn(scene, "Enemy", position)</c>, or <c>Game.SpawnPrefab("Enemy", position)</c> from a script. In the
/// editor, select an object and press "Save as prefab" in the Inspector; the Assets tab lists the prefabs.
/// </summary>
public static class Prefab
{
    public const string Extension = ".prefab";

    private static readonly Dictionary<string, (DateTime stamp, PrefabData data)> Cache = new(StringComparer.OrdinalIgnoreCase);

    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>
    /// Folders searched for prefab files, first match wins. By default the Prefabs folder next to the executable; the
    /// editor puts the project's own Prefabs folder in front, so a prefab saved a second ago is found.
    /// </summary>
    public static List<string> SearchFolders { get; } = new() { Path.Combine(AppContext.BaseDirectory, "Prefabs") };

    /// <summary>
    /// Finds a script class by its full name. The editor replaces it so prefabs get the live-compiled scripts; by
    /// default every loaded assembly is searched.
    /// </summary>
    public static Func<string, Type?> ScriptTypeResolver { get; set; } = DefaultScriptType;

    /// <summary>The names of every prefab found in <see cref="SearchFolders"/>.</summary>
    public static IEnumerable<string> Names =>
        SearchFolders.Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*" + Extension))
            .Select(Path.GetFileNameWithoutExtension)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)!;

    /// <summary>The prefab's file, or null if there is none.</summary>
    public static string? FindFile(string name) =>
        SearchFolders.Select(folder => Path.Combine(folder, name + Extension)).FirstOrDefault(File.Exists);

    public static bool Exists(string name) => FindFile(name) != null;

    /// <summary>Reads a prefab (cached until its file changes). Null, with a warning, if it is missing or broken.</summary>
    public static PrefabData? Load(string name)
    {
        var file = FindFile(name);
        if (file == null)
        {
            Log.Warn($"Prefab '{name}' not found (looked in {string.Join(", ", SearchFolders)}).");
            return null;
        }

        try
        {
            var stamp = File.GetLastWriteTimeUtc(file);
            if (Cache.TryGetValue(file, out var cached) && cached.stamp == stamp) return cached.data;
            var data = JsonSerializer.Deserialize<PrefabData>(File.ReadAllText(file), Json);
            if (data?.Root == null) throw new JsonException("no root object");
            Cache[file] = (stamp, data);
            return data;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn($"Prefab '{name}' could not be read: {e.Message}");
            return null;
        }
    }

    /// <summary>A new copy of the prefab at <paramref name="position"/>, not yet in any scene. Null if it cannot be loaded.</summary>
    public static GameObject? Instantiate(string name, Vector3 position)
    {
        var data = Load(name);
        if (data == null) return null;
        var obj = Build(data.Root!);
        obj.Position = position;
        obj.PrefabName = data.Name ?? name;
        return obj;
    }

    /// <summary>Instantiates the prefab and adds it to <paramref name="scene"/>. Null if it cannot be loaded.</summary>
    public static GameObject? Spawn(Scene scene, string name, Vector3 position)
    {
        var obj = Instantiate(name, position);
        if (obj != null) scene.Add(obj);
        return obj;
    }

    // ---- building objects ------------------------------------------------------------------------

    /// <summary>Creates the object described by <paramref name="data"/> (and its children).</summary>
    public static GameObject Build(PrefabObject data)
    {
        var obj = new GameObject(BuildModel(data), ToVector(data.Position, Vector3.Zero))
        {
            Name = data.Name ?? "Prefab",
            Tag = data.Tag ?? "",
            Enabled = data.Enabled,
            Visible = data.Visible,
            CastShadow = data.CastShadow,
            ReceiveShadow = data.ReceiveShadow,
            Wireframe = data.Wireframe,
            CullFace = data.CullFace,
            Collision = Enum.TryParse<CollisionMode>(data.Collision, true, out var mode) ? mode : CollisionMode.Mesh,
        };
        obj.EulerAngles = ToVector(data.Rotation, Vector3.Zero);
        obj.Scale = ToVector(data.Scale, Vector3.One);

        if (data.Material != null)
            foreach (var (key, value) in data.Material)
                ParameterAccess.TrySet(obj.Material, key, FromJson(value));
        if (!string.IsNullOrEmpty(data.Texture))
        {
            try { obj.SetTexture(data.Texture); }
            catch (Exception e) when (e is IOException or InvalidOperationException) { Log.Warn($"Prefab texture '{data.Texture}': {e.Message}"); }
        }
        if (!string.IsNullOrEmpty(data.Shader)) obj.SetShader(data.Shader);
        if (data.ShaderParams != null)
            foreach (var (key, v) in data.ShaderParams)
                obj.SetShaderParam(key, v.ElementAtOrDefault(0), v.ElementAtOrDefault(1), v.ElementAtOrDefault(2));
        if (data.ShaderTextures != null)
            foreach (var (key, path) in data.ShaderTextures) obj.SetShaderTexture(key, path);

        foreach (var scriptData in data.Scripts ?? new List<PrefabScript>())
        {
            var type = scriptData.Type == null ? null : ScriptTypeResolver(scriptData.Type);
            if (type == null)
            {
                Log.Warn($"Prefab object '{obj.Name}': script '{scriptData.Type}' not found, skipped.");
                continue;
            }
            var script = obj.AddScript(type);
            if (scriptData.Values != null)
                foreach (var (key, value) in scriptData.Values)
                    ParameterAccess.TrySet(script, key, FromJson(value));
        }

        foreach (var child in data.Children ?? new List<PrefabObject>()) obj.AddChild(Build(child));
        return obj;
    }

    private static Model BuildModel(PrefabObject data)
    {
        if (!string.IsNullOrEmpty(data.Model))
        {
            var model = Model.TryLoad(data.Model);
            if (model != null) return model;
        }
        if (!string.IsNullOrEmpty(data.Primitive) && FromRecipe(data.Primitive) is { } mesh) return Model.FromMesh(mesh);
        if (data.Vertices != null && data.Indices != null)
        {
            var vertices = MemoryMarshal.Cast<byte, Vertex>(Convert.FromBase64String(data.Vertices)).ToArray();
            var indices = MemoryMarshal.Cast<byte, uint>(Convert.FromBase64String(data.Indices)).ToArray();
            return Model.FromMesh(new Mesh(vertices, indices));
        }
        Log.Warn($"Prefab object '{data.Name}' has no usable mesh; a cube stands in for it.");
        return Model.FromMesh(Primitives.Cube());
    }

    /// <summary>
    /// Rebuilds a primitive from the recipe it remembers (<see cref="Mesh.Origin"/>), e.g.
    /// <c>"Primitives.Capsule(0.35f, 1.8f, 24)"</c>. Null if the recipe is not a plain Primitives call.
    /// </summary>
    public static Mesh? FromRecipe(string recipe)
    {
        var match = Regex.Match(recipe.Trim(), @"^(?:GT67\.Utils\.)?Primitives\.(\w+)\((.*)\)$");
        if (!match.Success) return null;

        var args = match.Groups[2].Value.Length == 0
            ? Array.Empty<string>()
            : match.Groups[2].Value.Split(',').Select(a => a.Trim().TrimEnd('f', 'F')).ToArray();
        foreach (var method in typeof(Primitives).GetMethods().Where(m => m.Name == match.Groups[1].Value && m.ReturnType == typeof(Mesh)))
        {
            var parameters = method.GetParameters();
            if (args.Length > parameters.Length || parameters.Skip(args.Length).Any(p => !p.HasDefaultValue)) continue;

            var values = new object?[parameters.Length];
            bool ok = true;
            for (int i = 0; i < parameters.Length && ok; i++)
            {
                var type = parameters[i].ParameterType;
                if (i >= args.Length) values[i] = parameters[i].DefaultValue;
                else if (type == typeof(float) && float.TryParse(args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) values[i] = f;
                else if (type == typeof(int) && int.TryParse(args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) values[i] = n;
                else ok = false;
            }
            if (ok) return (Mesh)method.Invoke(null, values)!;
        }
        return null;
    }

    // ---- values ----------------------------------------------------------------------------------

    /// <summary>A parameter value as something JSON can hold: numbers, bools, enum names, float arrays for vectors and colours.</summary>
    public static object? ToJson(object? value) => value switch
    {
        null => null,
        Enum e => e.ToString(),
        Vector2 v => new[] { v.X, v.Y },
        Vector3 v => new[] { v.X, v.Y, v.Z },
        Vector4 v => new[] { v.X, v.Y, v.Z, v.W },
        Color4 c => new[] { c.R, c.G, c.B, c.A },
        _ => value,
    };

    /// <summary>The reverse of <see cref="ToJson"/>: a value <see cref="ParameterAccess"/> can convert.</summary>
    private static object? FromJson(object? value)
    {
        if (value is not JsonElement e) return value;
        return e.ValueKind switch
        {
            JsonValueKind.Number => e.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Array => e.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? (float)x.GetDouble() : 0f).ToArray(),
            _ => null,
        };
    }

    internal static float[] ToArray(Vector3 v) => new[] { v.X, v.Y, v.Z };

    private static Vector3 ToVector(float[]? values, Vector3 fallback) =>
        values is { Length: 3 } ? new Vector3(values[0], values[1], values[2]) : fallback;

    private static Type? DefaultScriptType(string fullName)
    {
        // The last loaded assembly wins, so newer copies of a script are preferred.
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Reverse())
            if (assembly.GetType(fullName) is { } type && type.IsSubclassOf(typeof(Scripting.Script))) return type;
        return null;
    }
}

/// <summary>The contents of a .prefab file.</summary>
public sealed class PrefabData
{
    public int Version { get; set; } = 1;
    public string? Name { get; set; }
    public PrefabObject? Root { get; set; }
}

/// <summary>One object of a prefab (the root or a child).</summary>
public sealed class PrefabObject
{
    public string? Name { get; set; }
    public string? Tag { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Visible { get; set; } = true;
    public bool CastShadow { get; set; } = true;
    public bool ReceiveShadow { get; set; } = true;
    public bool Wireframe { get; set; }
    public bool CullFace { get; set; } = true;
    public string? Collision { get; set; }

    /// <summary>Local position (ignored for the root, which is placed where it is spawned), Euler angles in degrees, scale.</summary>
    public float[]? Position { get; set; }
    public float[]? Rotation { get; set; }
    public float[]? Scale { get; set; }

    /// <summary>The mesh: a model file (relative to the executable), else a primitive recipe, else baked vertices (base64).</summary>
    public string? Model { get; set; }
    public string? Primitive { get; set; }
    public string? Vertices { get; set; }
    public string? Indices { get; set; }

    /// <summary>Material settings by name (only those that differ from the defaults), diffuse texture, custom shader.</summary>
    public Dictionary<string, object?>? Material { get; set; }
    public string? Texture { get; set; }
    public string? Shader { get; set; }
    public Dictionary<string, float[]>? ShaderParams { get; set; }
    public Dictionary<string, string>? ShaderTextures { get; set; }

    public List<PrefabScript>? Scripts { get; set; }
    public List<PrefabObject>? Children { get; set; }
}

/// <summary>A script attached to a prefab object: its class and the settings that differ from the class defaults.</summary>
public sealed class PrefabScript
{
    public string? Type { get; set; }
    public Dictionary<string, object?>? Values { get; set; }
}
