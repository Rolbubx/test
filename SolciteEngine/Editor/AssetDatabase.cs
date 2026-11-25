using System.Text.Json;

namespace SolciteEngine.Editor;

/// <summary>
/// Gives every file in a project's <c>assets/</c> folder a companion <c>&lt;file&gt;.reference</c> that holds its identity (a GUID,
/// its kind, when it was first seen), like Unity's .meta files: <c>assets/Models/tree.glb</c> has
/// <c>assets/Models/tree.glb.reference</c>. Keep the two files together when moving an asset so it keeps its identity. The
/// editor also writes <c>cache/asset-index.json</c> (GUID to path), which it can rebuild at any time.
/// </summary>
public static class AssetDatabase
{
    public const string ReferenceExtension = ".reference";
    private const string IndexFile = "asset-index.json";

    /// <summary>The content of a <c>.reference</c> file.</summary>
    public sealed class Reference
    {
        public string Guid { get; set; } = "";
        public string Type { get; set; } = "File";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <summary>The kind of asset a file is, from its extension.</summary>
    public static string TypeOf(string file)
    {
        string extension = Path.GetExtension(file).ToLowerInvariant();
        if (ModelImporter.IsModelFile(file)) return "Model";
        if (Core.AudioClip.IsAudioFile(file)) return "Sound";
        return extension switch
        {
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".hdr" => "Texture",
            ".cs" => "Script",
            ".shader" or ".frag" or ".vert" => "Shader",
            ".prefab" => "Prefab",
            ".mesh" => "Mesh",
            _ => "File",
        };
    }

    /// <summary>The <c>.reference</c> file of an asset.</summary>
    public static string ReferenceFileOf(string assetFile) => assetFile + ReferenceExtension;

    /// <summary>Reads the reference of an asset, or null if it has none.</summary>
    public static Reference? Read(string assetFile)
    {
        try
        {
            string file = ReferenceFileOf(assetFile);
            return File.Exists(file) ? JsonSerializer.Deserialize<Reference>(File.ReadAllText(file)) : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Brings the references up to date: creates the missing ones, removes those whose asset is gone, and rewrites the index
    /// in <c>cache/</c> if it changed. Returns how many references were created. Does nothing for the engine sandbox.
    /// </summary>
    public static int Sync(ProjectPaths project)
    {
        if (project.IsEngineSandbox || !Directory.Exists(project.Assets)) return 0;

        int created = 0;
        var index = new SortedDictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var file in Directory.EnumerateFiles(project.Assets, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith('.')) continue;

                if (name.EndsWith(ReferenceExtension, StringComparison.OrdinalIgnoreCase))
                {
                    // an orphan: its asset was deleted or renamed without it
                    if (!File.Exists(file[..^ReferenceExtension.Length])) File.Delete(file);
                    continue;
                }

                var reference = Read(file);
                if (reference == null || reference.Guid.Length == 0)
                {
                    reference = new Reference { Guid = Guid.NewGuid().ToString("N"), Type = TypeOf(file) };
                    File.WriteAllText(ReferenceFileOf(file), JsonSerializer.Serialize(reference, Pretty));
                    created++;
                }
                // two files with the same GUID (a copied pair): the copy gets a new identity
                if (index.ContainsKey(reference.Guid))
                {
                    reference.Guid = Guid.NewGuid().ToString("N");
                    File.WriteAllText(ReferenceFileOf(file), JsonSerializer.Serialize(reference, Pretty));
                }
                index[reference.Guid] = Path.GetRelativePath(project.Assets, file).Replace('\\', '/');
            }

            Directory.CreateDirectory(project.Cache);
            string indexPath = Path.Combine(project.Cache, IndexFile);
            string json = JsonSerializer.Serialize(index, Pretty);
            if (!File.Exists(indexPath) || File.ReadAllText(indexPath) != json) File.WriteAllText(indexPath, json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Utils.Log.Warn($"Could not update the asset references: {e.Message}");
        }
        return created;
    }
}
