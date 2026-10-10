using GT67.Core;

namespace GT67.Utils;

/// <summary>Global helpers (imported with <c>using static</c> in GlobalUsings.cs).</summary>
public static class Assets
{
    /// <summary>Loads a model through the cache, so the same file is only loaded once.</summary>
    public static Model LoadModel(string path) => Model.Load(path);

    /// <summary>True when the (relative) asset path exists next to the executable.</summary>
    public static bool Exists(string path) =>
        File.Exists(ModelCache.ResolvePath(path)) || Directory.Exists(ModelCache.ResolvePath(path));
}
