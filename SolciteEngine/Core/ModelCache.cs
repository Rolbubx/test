using SolciteEngine.Utils;

namespace SolciteEngine.Core;

/// <summary>
/// The one intentional static in the engine: a path-keyed cache for models and shared textures.
/// Call <see cref="Clear"/> before the GL context goes away (the Window does this on unload).
/// </summary>
public static class ModelCache
{
    private static readonly Dictionary<string, Model> Models = new();
    private static readonly Dictionary<string, Texture> Textures = new();

    public static int ModelCount => Models.Count;

    public static Model GetModel(string path)
    {
        string full = ResolvePath(path);
        if (Models.TryGetValue(full, out var cached)) return cached;

        var model = ModelLoader.Load(full);   // logs its own timing
        model.Retain(); // the cache's own reference
        Models[full] = model;
        model.PrepareCollision();   // in the background, ready by the time something collides with it
        return model;
    }

    /// <summary>Makes an already loaded model available under a second path too (the copy the editor made of it).</summary>
    public static void Alias(string path, Model model)
    {
        string full = ResolvePath(path);
        if (Models.ContainsKey(full)) return;
        model.Retain();
        Models[full] = model;
    }

    public static Texture GetTexture(string path, bool srgb = true)
    {
        string full = ResolvePath(path);
        string key = (srgb ? "s:" : "l:") + full;
        if (Textures.TryGetValue(key, out var cached)) return cached;

        var texture = Texture.FromFile(full, srgb);
        Textures[key] = texture;
        return texture;
    }

    /// <summary>Hands a procedurally created texture to the cache so it is freed on <see cref="Clear"/>.</summary>
    public static Texture Register(string key, Texture texture)
    {
        Textures[key] = texture;
        return texture;
    }

    /// <summary>
    /// The folder of the project the editor has open (null in the game). Relative paths are looked for there first, so a
    /// project's own Assets/ and its compiled game's Assets/Game/ are found without being copied next to the executable.
    /// </summary>
    public static string? ProjectRoot { get; set; }

    /// <summary>The open project's assets folder (<c>Assets/...</c> paths are looked for here) and the assets its generated game ships (<c>Assets/Game/...</c>).</summary>
    public static string? ProjectAssets { get; set; }
    public static string? ProjectGameAssets { get; set; }

    /// <summary>Relative paths are resolved against the open project, then against the executable folder, where assets are copied.</summary>
    public static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        if (ProjectRoot != null)
        {
            const string game = "Assets/Game/", assets = "Assets/";
            string normalized = path.Replace('\\', '/');
            string inProject =
                normalized.StartsWith(game, StringComparison.OrdinalIgnoreCase) && ProjectGameAssets != null ? Path.Combine(ProjectGameAssets, normalized[game.Length..])
                : normalized.StartsWith(assets, StringComparison.OrdinalIgnoreCase) && ProjectAssets != null ? Path.Combine(ProjectAssets, normalized[assets.Length..])
                : Path.Combine(ProjectRoot, normalized);
            if (File.Exists(inProject) || Directory.Exists(inProject)) return Path.GetFullPath(inProject);
        }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    public static void Clear()
    {
        foreach (var model in Models.Values) model.Dispose();
        foreach (var texture in Textures.Values) texture.Dispose();
        Models.Clear();
        Textures.Clear();
    }
}
