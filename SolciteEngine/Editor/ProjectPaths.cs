using System.Text.Json;

namespace SolciteEngine.Editor;

/// <summary>
/// Where the project's source folders are. The editor writes scripts, the generated map and imported assets there
/// (not into the build output folder, which is wiped by a rebuild).
/// </summary>
public sealed class ProjectPaths
{
    /// <summary>The file that marks a folder as a SolciteEngine project (name, engine, creation date).</summary>
    public const string ProjectFileName = "infos.json";

    /// <summary>Prefix of the folder a new project is created in: <c>Project-&lt;name&gt;</c>.</summary>
    public const string FolderPrefix = "Project-";

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

    /// <summary>True for the engine's own folder, which the editor can still open as a sandbox project (it keeps its older layout).</summary>
    public bool IsEngineSandbox { get; }

    /// <summary>The SolciteEngine.csproj that the generated game builds against (null when the engine sources are not around).</summary>
    public string? EngineProject => LocateEngineProject();

    // ---- layout ---------------------------------------------------------------------------------
    //   Project-<name>/  infos.json  generated-game/  assets/ (Scripts, Models, Textures, Sounds, Skybox, Prefabs, Shaders/Custom)  cache/
    // The engine sandbox is the engine's own folder: Scripts/, Assets/, Prefabs/, Shaders/Custom and Generated/GeneratedGame.

    /// <summary>Everything the user puts in the project: scripts, models, textures, sounds, prefabs and shaders.</summary>
    public string Assets => IsEngineSandbox ? Path.Combine(Root, "Assets") : Path.Combine(Root, "assets");
    public string Scripts => IsEngineSandbox ? Path.Combine(Root, "Scripts") : Path.Combine(Assets, "Scripts");
    public string Models => Path.Combine(Assets, "Models");
    public string Textures => Path.Combine(Assets, "Textures");
    public string Sounds => Path.Combine(Assets, "Sounds");
    public string Skybox => Path.Combine(Assets, "Skybox");
    public string Prefabs => IsEngineSandbox ? Path.Combine(Root, "Prefabs") : Path.Combine(Assets, "Prefabs");
    public string CustomShaders => IsEngineSandbox ? Path.Combine(Root, "Shaders", "Custom") : Path.Combine(Assets, "Shaders", "Custom");

    /// <summary>The generated game: GeneratedGame.csproj, the one Game.cs and the assets it loads (Compile rewrites the whole folder).</summary>
    public string GeneratedGame => IsEngineSandbox ? Path.Combine(Root, "Generated", "GeneratedGame") : Path.Combine(Root, "generated-game");
    public string GeneratedGameFile => Path.Combine(GeneratedGame, "Game.cs");

    /// <summary>Files the editor can rebuild at any time (the asset index, editor settings); safe to delete.</summary>
    public string Cache => IsEngineSandbox ? Root : Path.Combine(Root, "cache");

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

    /// <summary>The content of <c>infos.json</c>.</summary>
    private sealed class ProjectFile
    {
        public string Name { get; set; } = "";
        public string Engine { get; set; } = "SolciteEngine";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>True if <paramref name="folder"/> holds a project file.</summary>
    public static bool IsProjectFolder(string folder) => File.Exists(Path.Combine(folder, ProjectFileName));

    /// <summary>Opens the project in <paramref name="folder"/> (it must contain infos.json).</summary>
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
    /// Creates the project <paramref name="name"/> in <c>&lt;location&gt;/Project-&lt;name&gt;</c>: <c>infos.json</c>, <c>assets/</c>
    /// (Scripts with the shared usings and a first script, Models, Textures, Sounds, Skybox, Prefabs, Shaders/Custom),
    /// <c>generated-game/</c> and <c>cache/</c>. The folder must not exist yet or must be empty.
    /// </summary>
    public static ProjectPaths? Create(string location, string name, out string error)
    {
        error = "";
        string clean = CleanName(name);
        if (clean.Length == 0) { error = "Give the project a name (letters, digits, spaces, '_' and '-')."; return null; }
        if (string.IsNullOrWhiteSpace(location)) { error = "Choose where to create the project."; return null; }

        try
        {
            string folder = Path.GetFullPath(Path.Combine(location.Trim().Trim('"'), FolderPrefix + clean));
            if (File.Exists(Path.Combine(folder, ProjectFileName))) { error = "There already is a project in that folder."; return null; }
            if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) { error = "That folder already exists and is not empty."; return null; }

            Directory.CreateDirectory(folder);
            var project = new ProjectPaths(folder, clean, isEngineSandbox: false);
            foreach (var sub in new[] { project.Scripts, project.Models, project.Textures, project.Sounds, project.Skybox, project.Prefabs,
                                        project.CustomShaders, project.GeneratedGame, project.Cache })
                Directory.CreateDirectory(sub);

            File.WriteAllText(Path.Combine(folder, ProjectFileName),
                JsonSerializer.Serialize(new ProjectFile { Name = clean }, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(project.Scripts, "_Usings.cs"), UsingsTemplate);
            File.WriteAllText(Path.Combine(project.Scripts, "MyScript.cs"), ScriptTemplate);
            File.WriteAllText(Path.Combine(folder, ".gitignore"), "cache/\ngenerated-game/bin/\ngenerated-game/obj/\ngenerated-game.new/\ngenerated-game.old/\n");
            AssetDatabase.Sync(project);
            return project;
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
                return Normalize(full[prefix.Length..].Replace('\\', '/'));
        }
        return null;
    }

    /// <summary>
    /// The paths the engine stores in a game are always <c>Assets/...</c> and <c>Generated/GeneratedGame/...</c>; a project's
    /// folders are <c>assets/</c> and <c>generated-game/</c>, so they are translated.
    /// </summary>
    private string Normalize(string relative)
    {
        if (IsEngineSandbox) return relative;
        if (relative.StartsWith("assets/", StringComparison.Ordinal)) return "Assets/" + relative["assets/".Length..];
        if (relative.StartsWith("generated-game/", StringComparison.Ordinal)) return "Generated/GeneratedGame/" + relative["generated-game/".Length..];
        return relative;
    }
}
