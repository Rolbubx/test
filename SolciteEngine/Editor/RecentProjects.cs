using System.Text.Json;

namespace SolciteEngine.Editor;

/// <summary>The projects opened lately, kept per user in <c>recent-projects.json</c> in the engine's user data folder.</summary>
public static class RecentProjects
{
    public sealed record Entry(string Path, string Name, DateTime LastOpenedUtc);

    private const int MaxEntries = 12;

    private static string FilePath => Path.Combine(Core.ShaderCache.UserDataFolder, "recent-projects.json");

    /// <summary>The recent projects, most recent first. Folders that no longer hold a project are left out.</summary>
    public static List<Entry> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<Entry>();
            var list = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) ?? new List<Entry>();
            return list.Where(e => ProjectPaths.IsProjectFolder(e.Path)).OrderByDescending(e => e.LastOpenedUtc).ToList();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new List<Entry>();
        }
    }

    /// <summary>Puts <paramref name="project"/> at the top of the list.</summary>
    public static void Add(ProjectPaths project)
    {
        if (project.IsEngineSandbox) return;
        try
        {
            var list = Load().Where(e => !string.Equals(e.Path, project.Root, StringComparison.OrdinalIgnoreCase)).ToList();
            list.Insert(0, new Entry(project.Root, project.Name, DateTime.UtcNow));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list.Take(MaxEntries), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Utils.Log.Warn($"Could not save the recent projects: {e.Message}");
        }
    }

    /// <summary>Takes <paramref name="path"/> off the list.</summary>
    public static void Remove(string path)
    {
        try
        {
            var list = Load().Where(e => !string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)).ToList();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Utils.Log.Warn($"Could not save the recent projects: {e.Message}");
        }
    }
}
