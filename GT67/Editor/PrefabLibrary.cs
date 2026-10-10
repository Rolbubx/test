using System.Runtime.InteropServices;
using System.Text.Json;
using GT67.Core;
using GT67.Utils;
using OpenTK.Mathematics;

namespace GT67.Editor;

/// <summary>
/// The editor's side of <see cref="Prefab"/>: saves an object (and its children) as <c>Prefabs/&lt;Name&gt;.prefab</c>
/// in the project, copies the model and texture files it uses into the project's Assets folder so the game finds them,
/// and rebuilds the prefab's instances in the scene after a change.
/// </summary>
public sealed class PrefabLibrary
{
    private readonly ProjectPaths? _paths;
    private List<string> _names = new();
    private DateTime _lastScan = DateTime.MinValue;

    public PrefabLibrary(ProjectPaths? paths, ScriptWorkspace scripts)
    {
        _paths = paths;
        if (paths != null && !Prefab.SearchFolders.Contains(ProjectFolder, StringComparer.OrdinalIgnoreCase))
            Prefab.SearchFolders.Insert(0, ProjectFolder);

        // Prefabs get the live-compiled version of their scripts (the same classes the Inspector attaches).
        var fallback = Prefab.ScriptTypeResolver;
        Prefab.ScriptTypeResolver = name => scripts.Types.FirstOrDefault(t => t.FullName == name) ?? fallback(name);
    }

    private string ProjectFolder => Path.Combine(_paths!.Root, "Prefabs");
    private static string OutputFolder => Path.Combine(AppContext.BaseDirectory, "Prefabs");

    /// <summary>The prefab names (the folders are scanned at most every two seconds).</summary>
    public IReadOnlyList<string> Names
    {
        get
        {
            if ((DateTime.Now - _lastScan).TotalSeconds >= 2)
            {
                _lastScan = DateTime.Now;
                _names = Prefab.Names.ToList();
            }
            return _names;
        }
    }

    /// <summary>Letters, digits, spaces, '_' and '-' only (it becomes a file name).</summary>
    public static string CleanName(string name) =>
        new string(name.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-').ToArray()).Trim();

    // ---- saving ----------------------------------------------------------------------------------

    /// <summary>
    /// Writes <paramref name="obj"/> as the prefab <paramref name="name"/> (replacing it if it exists) and links the
    /// object to it. <paramref name="warnings"/> collects what could not be kept (generated textures...).
    /// </summary>
    public bool Save(GameObject obj, string name, List<string> warnings, out string message)
    {
        name = CleanName(name);
        if (name.Length == 0)
        {
            message = "Give the prefab a name (letters, digits, spaces, _ and -).";
            return false;
        }

        try
        {
            var data = new PrefabData { Name = name, Root = Capture(obj, root: true, warnings) };
            string json = JsonSerializer.Serialize(data, Prefab.Json);
            // Into the project (kept, and copied next to the game by the build) and the build output (usable right now).
            var folders = _paths != null ? new[] { ProjectFolder, OutputFolder } : new[] { OutputFolder };
            foreach (var folder in folders)
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, name + Prefab.Extension), json);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            message = $"Could not save the prefab '{name}': {e.Message}";
            return false;
        }

        obj.PrefabName = name;
        _lastScan = DateTime.MinValue;
        message = $"Saved prefab '{name}' (Prefabs/{name}{Prefab.Extension}).";
        return true;
    }

    private PrefabObject Capture(GameObject obj, bool root, List<string> warnings)
    {
        var data = new PrefabObject
        {
            Name = obj.Name,
            Tag = obj.Tag.Length > 0 ? obj.Tag : null,
            Enabled = obj.Enabled,
            Visible = obj.Visible,
            CastShadow = obj.CastShadow,
            ReceiveShadow = obj.ReceiveShadow,
            Wireframe = obj.Wireframe,
            CullFace = obj.CullFace,
            Collision = obj.Collision.ToString(),
            Position = root ? null : Prefab.ToArray(obj.Position),
            Rotation = obj.EulerAngles == Vector3.Zero ? null : Prefab.ToArray(obj.EulerAngles),
            Scale = obj.Scale == Vector3.One ? null : Prefab.ToArray(obj.Scale),
        };

        CaptureMesh(obj, data, warnings);

        var material = obj.Material;
        var defaults = new Material();
        foreach (var parameter in ParameterAccess.Describe(material))
            if (!Equals(parameter.Value, ParameterAccess.Get(defaults, parameter.Name)))
                (data.Material ??= new())[parameter.Name] = Prefab.ToJson(parameter.Value);
        if (material.DiffuseMap is { } texture)
        {
            data.Texture = AssetPath(texture.Path, "Textures");
            if (data.Texture == null) warnings.Add($"'{obj.Name}': its texture is generated in code, not a file, so the prefab leaves it out.");
        }
        if (material.CustomShader.Length > 0) data.Shader = material.CustomShader;
        foreach (var (key, v) in material.ShaderParams) (data.ShaderParams ??= new())[key] = new[] { v.X, v.Y, v.Z };

        foreach (var script in obj.Scripts)
        {
            var type = script.GetType();
            object? scriptDefaults = null;
            try { scriptDefaults = Activator.CreateInstance(type); } catch (MissingMethodException) { }

            var entry = new PrefabScript { Type = type.FullName };
            foreach (var parameter in ParameterAccess.Describe(script))
                if (scriptDefaults == null || !Equals(parameter.Value, ParameterAccess.Get(scriptDefaults, parameter.Name)))
                    (entry.Values ??= new())[parameter.Name] = Prefab.ToJson(parameter.Value);
            (data.Scripts ??= new()).Add(entry);
        }

        foreach (var child in obj.Children)
            if (!child.EditorOnly) (data.Children ??= new()).Add(Capture(child, root: false, warnings));
        return data;
    }

    private void CaptureMesh(GameObject obj, PrefabObject data, List<string> warnings)
    {
        var model = obj.Model;
        if (model.SourcePath != null && ModelPath(model) is { } path)
        {
            data.Model = path;
            return;
        }

        var mesh = model.Parts[0].Mesh;
        if (model.Parts.Count == 1 && mesh.Origin is { } origin && Prefab.FromRecipe(origin) != null)
        {
            data.Primitive = origin;
            return;
        }

        // No file and no recipe (a heightmap, an edited mesh): store the vertices themselves.
        if (model.Parts.Count > 1) warnings.Add($"'{obj.Name}' has {model.Parts.Count} parts and no model file; only the first part was saved.");
        data.Vertices = Convert.ToBase64String(MemoryMarshal.AsBytes<Vertex>(mesh.Vertices));
        data.Indices = Convert.ToBase64String(MemoryMarshal.AsBytes<uint>(mesh.Indices));
    }

    // ---- asset files -----------------------------------------------------------------------------

    /// <summary>
    /// The path (relative to the executable) the game loads the model from. A model from outside the project is copied,
    /// with the files it needs, into Assets/Models/&lt;name&gt;/ first.
    /// </summary>
    private string? ModelPath(Model model)
    {
        string source = model.SourcePath!;
        if (GamePath(source) is { } known) return known;

        string folder = Path.GetFileNameWithoutExtension(source);
        string modelDirectory = Path.GetDirectoryName(source)!;
        try
        {
            foreach (var file in model.SourceFiles.Append(source).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string inside = Path.GetRelativePath(modelDirectory, file);
                if (inside.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(inside)) inside = Path.GetFileName(file);
                CopyIntoAssets(file, Path.Combine("Models", folder, inside));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not copy '{source}' into the project: {e.Message}");
            return null;
        }
        return $"Assets/Models/{folder}/{Path.GetFileName(source)}";
    }

    /// <summary>Same for a single file (a texture): its game path, after copying it into Assets/&lt;category&gt;/ if needed.</summary>
    private string? AssetPath(string? file, string category)
    {
        if (string.IsNullOrEmpty(file)) return null;
        string full = Path.IsPathRooted(file) ? file : ModelCache.ResolvePath(file);
        if (!File.Exists(full)) return null;
        if (GamePath(full) is { } known) return known;

        try
        {
            CopyIntoAssets(full, Path.Combine(category, Path.GetFileName(full)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not copy '{file}' into the project: {e.Message}");
            return null;
        }
        return $"Assets/{category}/{Path.GetFileName(full)}";
    }

    /// <summary>
    /// The path relative to the executable if the file already ships with the game: anything under Assets/ (in the
    /// build output or the project) or a file the editor's Compile shipped (Generated/GeneratedGame/Assets).
    /// </summary>
    private string? GamePath(string file)
    {
        string? relative = _paths?.ToProjectRelative(file);
        if (relative == null) return null;
        if (relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return relative;
        const string generated = "Generated/GeneratedGame/Assets/";
        if (relative.StartsWith(generated, StringComparison.OrdinalIgnoreCase)) return "Assets/Game/" + relative[generated.Length..];
        return null;
    }

    /// <summary>Copies into the project's Assets (kept) and the build output's Assets (loadable right away).</summary>
    private void CopyIntoAssets(string from, string relativeTarget)
    {
        foreach (var root in new[] { _paths?.Assets, Path.Combine(AppContext.BaseDirectory, "Assets") })
        {
            if (root == null) continue;
            string to = Path.Combine(root, relativeTarget);
            if (string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(from, to, overwrite: true);
        }
    }

    // ---- instances -------------------------------------------------------------------------------

    /// <summary>
    /// Replaces <paramref name="instance"/> with a fresh copy of its prefab, keeping its name, place, rotation and
    /// parent. Returns the new object, or null if the prefab cannot be loaded.
    /// </summary>
    public static GameObject? Rebuild(Scene scene, GameObject instance)
    {
        if (instance.PrefabName == null) return null;
        var fresh = Prefab.Instantiate(instance.PrefabName, instance.Position);
        if (fresh == null) return null;

        fresh.Name = instance.Name;
        fresh.Rotation = instance.Rotation;
        var parent = instance.Parent;
        instance.Destroy();
        if (parent != null) parent.AddChild(fresh);
        else scene.Add(fresh);
        return fresh;
    }

    /// <summary>Rebuilds every instance of the prefab in the scene except <paramref name="except"/>. Returns how many.</summary>
    public static int RebuildInstances(Scene scene, string name, GameObject? except)
    {
        var instances = scene.Traverse()
            .Where(o => o != except && !o.EditorOnly && string.Equals(o.PrefabName, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        int count = 0;
        foreach (var instance in instances)
            if (!instance.IsDestroyed && Rebuild(scene, instance) != null) count++;
        return count;
    }
}
