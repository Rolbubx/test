using System.Text.Json;

namespace SolciteEngine.Editor;

/// <summary>
/// Where the project's source folders are. The editor writes scripts, the generated map and imported assets there
/// (not into the build output folder, which is wiped by a rebuild).
/// </summary>
public sealed class ProjectPaths
{
    /// <summary>The file that marks a folder as a SolciteEngine project.</summary>
    public const string ProjectFileName = "project.solcite.json";

    private ProjectPaths(string root, string name, bool isEngineSandbox)
    {
        Root = root;
        Name = name;
        IsEngineSandbox = isEngineSandbox;
    }

    /// <summary>The project folder (a user project), or the folder that contains SolciteEngine.csproj (the engine sandbox).</summary>
    public string Root { get; }

    /// <summary>The project's name (shown in the launcher and in the window title).</summary>
    public string Name { get; }

    /// <summary>True for the engine's own folder, which the editor can still open as a sandbox project.</summary>
    public bool IsEngineSandbox { get; }

    /// <summary>The SolciteEngine.csproj that the generated game builds against (null when the engine sources are not around).</summary>
    public string? EngineProject => LocateEngineProject();

    public string Scripts => Path.Combine(Root, "Scripts");
    public string Generated => Path.Combine(Root, "Generated");
    /// <summary>The generated game: GeneratedGame.csproj, the one Game.cs and the assets it loads (Compile rewrites the whole folder).</summary>
    public string GeneratedGame => Path.Combine(Generated, "GeneratedGame");
    public string GeneratedGameFile => Path.Combine(GeneratedGame, "Game.cs");
    public string Assets => Path.Combine(Root, "Assets");
    public string Models => Path.Combine(Assets, "Models");
    public string Textures => Path.Combine(Assets, "Textures");

    /// <summary>Searches upwards from the executable (and the working directory) for SolciteEngine.csproj. Null if not found.</summary>
    private static string? LocateEngineProject()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "SolciteEngine.csproj")))
                    return Path.Combine(dir.FullName, "SolciteEngine.csproj");
        }
        return null;
    }

    /// <summary>The engine's own folder as a sandbox project (what the editor edited before there were projects). Null if not found.</summary>
    public static ProjectPaths? Locate() =>
        LocateEngineProject() is { } csproj ? new ProjectPaths(Path.GetDirectoryName(csproj)!, "SolciteEngine sandbox", isEngineSandbox: true) : null;

    /// <summary>The content of <c>project.solcite.json</c>.</summary>
    private sealed class ProjectFile
    {
        public string Name { get; set; } = "";
        public string Engine { get; set; } = "SolciteEngine";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>True if <paramref name="folder"/> holds a project file.</summary>
    public static bool IsProjectFolder(string folder) => File.Exists(Path.Combine(folder, ProjectFileName));

    /// <summary>Opens the project in <paramref name="folder"/> (it must contain project.solcite.json).</summary>
    public static ProjectPaths? Open(string folder, out string error)
    {
        error = "";
        try
        {
            folder = Path.GetFullPath(folder.Trim().Trim('"'));
            string file = Path.Combine(folder, ProjectFileName);
            if (!Directory.Exists(folder)) { error = "That folder does not exist."; return null; }
            if (!File.Exists(file)) { error = $"No {ProjectFileName} in that folder: it is not a SolciteEngine project."; return null; }

            string name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
            try
            {
                var info = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(file));
                if (!string.IsNullOrWhiteSpace(info?.Name)) name = info!.Name;
            }
            catch (JsonException)
            {
                // a damaged project file: the folder name does as a name
            }
            return new ProjectPaths(folder, name, isEngineSandbox: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = e.Message;
            return null;
        }
    }

    /// <summary>Letters, digits, spaces, '_' and '-' only (it becomes a folder name).</summary>
    public static string CleanName(string name) =>
        new string(name.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-').ToArray()).Trim();

    /// <summary>The default parent folder of new projects (Documents/SolciteProjects).</summary>
    public static string DefaultLocation()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(documents)) documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(documents)) documents = Directory.GetCurrentDirectory();
        return Path.Combine(documents, "SolciteProjects");
    }

    /// <summary>
    /// Creates the project <paramref name="name"/> in <c>&lt;location&gt;/&lt;name&gt;</c>: the project file, Scripts (with the shared
    /// usings and a first script), Assets, Prefabs and Shaders/Custom. The folder must not exist yet or must be empty.
    /// </summary>
    public static ProjectPaths? Create(string location, string name, out string error)
    {
        error = "";
        string clean = CleanName(name);
        if (clean.Length == 0) { error = "Give the project a name (letters, digits, spaces, '_' and '-')."; return null; }
        if (string.IsNullOrWhiteSpace(location)) { error = "Choose where to create the project."; return null; }

        try
        {
            string folder = Path.GetFullPath(Path.Combine(location.Trim().Trim('"'), clean));
            if (File.Exists(Path.Combine(folder, ProjectFileName))) { error = "There already is a project in that folder."; return null; }
            if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) { error = "That folder already exists and is not empty."; return null; }

            Directory.CreateDirectory(folder);
            foreach (var sub in new[] { "Scripts", "Assets/Models", "Assets/Textures", "Assets/Sounds", "Assets/Skybox", "Prefabs", "Shaders/Custom" })
                Directory.CreateDirectory(Path.Combine(folder, sub));

            File.WriteAllText(Path.Combine(folder, ProjectFileName),
                JsonSerializer.Serialize(new ProjectFile { Name = clean }, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(folder, "Scripts", "_Usings.cs"), UsingsTemplate);
            File.WriteAllText(Path.Combine(folder, "Scripts", "MyScript.cs"), ScriptTemplate);
            File.WriteAllText(Path.Combine(folder, ".gitignore"), "Generated/GeneratedGame/bin/\nGenerated/GeneratedGame/obj/\nGenerated/GeneratedGame.new/\nGenerated/GeneratedGame.old/\n");
            return new ProjectPaths(folder, clean, isEngineSandbox: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = e.Message;
            return null;
        }
    }

    private const string UsingsTemplate = """
        // Shared by every script. The editor's live compiler uses these files too, so scripts need no `using` lines for
        // the engine, scripting API, OpenTK maths, keys or mouse buttons.
        global using SolciteEngine.Core;
        global using SolciteEngine.Scripting;
        global using OpenTK.Mathematics;
        global using Keys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;
        global using MouseButton = OpenTK.Windowing.GraphicsLibraryFramework.MouseButton;
        """;

    private const string ScriptTemplate = """
        /// <summary>A first script: drag it onto an object in the editor. Edit it in your code editor (Scripts/MyScript.cs).</summary>
        public class MyScript : Script
        {
            public float DegreesPerSecond = 45f;

            public override void OnUpdate(float deltaTime)
            {
                Self.RotateY(DegreesPerSecond * deltaTime);
            }
        }
        """;

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
