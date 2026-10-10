namespace GT67.Editor;

/// <summary>
/// Where the project's source folders are. The editor writes scripts, the generated map and imported assets there
/// (not into the build output folder, which is wiped by a rebuild).
/// </summary>
public sealed class ProjectPaths
{
    private ProjectPaths(string root) => Root = root;

    /// <summary>The folder that contains GT67.csproj.</summary>
    public string Root { get; }

    public string Scripts => Path.Combine(Root, "Scripts");
    public string Generated => Path.Combine(Root, "Generated");
    /// <summary>The generated game: several C# files plus the assets they load (Compile rewrites the whole folder).</summary>
    public string GeneratedGame => Path.Combine(Generated, "GeneratedGame");
    public string GeneratedGameFile => Path.Combine(GeneratedGame, "GameGenerator.cs");
    public string Assets => Path.Combine(Root, "Assets");
    public string Models => Path.Combine(Assets, "Models");
    public string Textures => Path.Combine(Assets, "Textures");

    /// <summary>Searches upwards from the executable (and the working directory) for GT67.csproj. Null if not found.</summary>
    public static ProjectPaths? Locate()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "GT67.csproj")))
                    return new ProjectPaths(dir.FullName);
        }
        return null;
    }

    /// <summary>
    /// Path relative to the project (forward slashes) if <paramref name="file"/> lies inside the project folder or
    /// the build output folder; otherwise null.
    /// </summary>
    public string? ToProjectRelative(string file)
    {
        string full = Path.GetFullPath(file);
        // The build output folder lies inside the project folder, so check it first.
        foreach (var root in new[] { AppContext.BaseDirectory, Root })
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return full[prefix.Length..].Replace('\\', '/');
        }
        return null;
    }
}
