using GT67.Core;

namespace GT67.Editor.Export;

/// <summary>
/// Copies everything the generated game loads (models with their textures, textures, sounds, UI images) into the
/// generated folder, so <c>Generated/GeneratedGame</c> is self-contained. The build copies that folder's Assets to
/// <c>Assets/Game/...</c> next to the executable, which is where the generated code loads them from.
/// </summary>
public sealed class AssetShipper
{
    /// <summary>The path prefix the generated code uses (relative to the executable).</summary>
    public const string GamePrefix = "Assets/Game";

    private readonly string _assetsRoot;
    private readonly ProjectPaths _paths;
    private readonly Dictionary<string, string> _shipped = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _usedNames = new(StringComparer.OrdinalIgnoreCase);

    public AssetShipper(string assetsRoot, ProjectPaths paths, List<string> warnings)
    {
        _assetsRoot = assetsRoot;
        _paths = paths;
        Warnings = warnings;
    }

    public List<string> Warnings { get; }

    /// <summary>How many files were copied.</summary>
    public int Copied { get; private set; }

    /// <summary>Ships a model file and every file it needs (textures, .bin, materials). Returns the path the game loads it from, or null.</summary>
    public string? ShipModel(Model model)
    {
        string source = model.SourcePath!;
        if (_shipped.TryGetValue(source, out var known)) return known;

        try
        {
            // A model that is already part of a shipped game keeps its folder name; anything else gets a fresh one.
            string? relative = _paths.ToProjectRelative(source);
            string folder;
            string shippedModels = GamePrefix + "/Models/";
            if (relative != null && relative.StartsWith(shippedModels, StringComparison.OrdinalIgnoreCase))
                folder = relative[shippedModels.Length..].Split('/')[0];
            else
                folder = UniqueName("Models", Path.GetFileNameWithoutExtension(source));

            string modelDirectory = Path.GetDirectoryName(source)!;
            string target = Path.Combine(_assetsRoot, "Models", folder);
            foreach (var file in model.SourceFiles)
            {
                string inside = Path.GetRelativePath(modelDirectory, file);
                if (inside.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(inside))
                    inside = Path.GetFileName(file); // a texture that lived elsewhere: put it beside the model
                Copy(file, Path.Combine(target, inside));
            }
            return _shipped[source] = $"{GamePrefix}/Models/{folder}/{Path.GetFileName(source)}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Warnings.Add($"Could not copy '{source}': {e.Message}");
            return null;
        }
    }

    public string? ShipTexture(Texture texture) => texture.Path != null ? ShipFile(texture.Path, "Textures") : null;

    /// <summary>Ships one file into <c>Assets/&lt;category&gt;</c> (a sound, a texture, a UI image). Returns the game path, or null if it cannot be read.</summary>
    public string? ShipFile(string file, string category)
    {
        string full = Path.IsPathRooted(file) ? file : ModelCache.ResolvePath(file);
        if (!File.Exists(full)) return null;
        if (_shipped.TryGetValue(full, out var known)) return known;

        try
        {
            // Already shipped by an earlier Compile (it lives in the build output as Assets/Game/<category>/<name>)?
            string? relative = _paths.ToProjectRelative(full);
            string prefix = $"{GamePrefix}/{category}/";
            string name = relative != null && relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? relative[prefix.Length..]
                : UniqueName(category, Path.GetFileName(full));

            Copy(full, Path.Combine(_assetsRoot, category, name));
            return _shipped[full] = prefix + name;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Warnings.Add($"Could not copy '{file}': {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Writes a new data file into <c>Assets/&lt;category&gt;</c> with <paramref name="write"/> (given the full path).
    /// Returns the game path, or null if it could not be written.
    /// </summary>
    public string? WriteData(string category, string name, Action<string> write)
    {
        string file = UniqueName(category, name);
        try
        {
            write(Path.Combine(_assetsRoot, category, file));
            Copied++;
            return $"{GamePrefix}/{category}/{file}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Warnings.Add($"Could not write '{category}/{file}': {e.Message}");
            return null;
        }
    }

    private string UniqueName(string category, string name)
    {
        string stem = Path.GetFileNameWithoutExtension(name), extension = Path.GetExtension(name);
        string candidate = stem + extension;
        for (int i = 2; !_usedNames.Add(category + "/" + candidate); i++) candidate = $"{stem}{i}{extension}";
        return candidate;
    }

    private void Copy(string from, string to)
    {
        if (!File.Exists(from)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to, overwrite: true);
        Copied++;
    }
}
