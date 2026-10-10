using GT67.Utils;

namespace GT67.Core;

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

    /// <summary>Relative paths are resolved against the executable folder, where assets are copied.</summary>
    public static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));

    public static void Clear()
    {
        foreach (var model in Models.Values) model.Dispose();
        foreach (var texture in Textures.Values) texture.Dispose();
        Models.Clear();
        Textures.Clear();
    }
}
