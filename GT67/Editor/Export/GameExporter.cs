using System.Text;
using GT67.Core;
using GT67.UI;
using static GT67.Editor.Export.CodeWriter;

namespace GT67.Editor.Export;

/// <summary>What <see cref="GameExporter"/> wrote.</summary>
public sealed record GenerateResult(string Folder, IReadOnlyList<string> Files, int Scenes, int Objects, int Terrains, int Waters,
    int UiElements, int Sounds, int CopiedFiles, IReadOnlyList<string> Warnings);

/// <summary>
/// The Compile button: turns the editor's scenes into a small game project in <c>Generated/GeneratedGame</c>.
/// <c>GameGenerator.cs</c> builds the game UI (<c>UiGenerator.cs</c>) and registers every scene with the
/// <see cref="SceneManager"/>, then loads the first one. Each scene has a folder <c>Scenes/&lt;Name&gt;</c> with one C# file
/// per topic, in its own namespace: <c>SceneGenerator</c> (calls the others), <c>EnvironmentGenerator</c> (sun, rain,
/// sky, lights), <c>PostProcessingGenerator</c> (the scene's Volume), <c>TerrainGenerator</c>, <c>GrassGenerator</c>,
/// <c>ObjectsGenerator</c>, <c>WaterGenerator</c> and <c>SoundsGenerator</c>. Every model (.fbx, .gltf, .obj, ...), texture,
/// sound and UI image the game uses is copied into <c>GeneratedGame/Assets</c>, which the build ships to
/// <c>Assets/Game</c> next to the executable.
/// </summary>
public sealed class GameExporter
{
    private readonly IReadOnlyList<Scene> _scenes;
    private readonly ProjectPaths _paths;
    private readonly UiCanvas _ui;

    /// <param name="scenes">The scenes in order; the first one is where the game starts.</param>
    public GameExporter(IReadOnlyList<Scene> scenes, ProjectPaths paths, UiCanvas ui)
    {
        _scenes = scenes;
        _paths = paths;
        _ui = ui;
    }

    public GenerateResult Generate()
    {
        // Write everything beside the old folder first: the editor's models may still be read from the old assets.
        string folder = _paths.GeneratedGame;
        string staging = folder + ".new";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);

        try
        {
            var warnings = new List<string>();
            var assets = new AssetShipper(Path.Combine(staging, "Assets"), _paths, warnings);
            var files = new List<(string path, string text)>();
            var registered = new List<(string name, string ns)>();
            int objects = 0, terrains = 0, waters = 0, sounds = 0;

            var usedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var scene in _scenes)
            {
                string id = Identifier(scene.Name);
                for (int i = 2; !usedFolders.Add(id); i++) id = Identifier(scene.Name) + i;
                string ns = $"{ExportContext.RootNamespace}.Scenes.{id}";
                var context = new ExportContext(scene, _paths, _ui, assets, warnings, ns);
                string dir = $"Scenes/{id}/";

                files.Add((dir + "SceneGenerator.cs", WriteSceneEntry(scene, ns)));
                files.Add((dir + EnvironmentExporter.FileName, EnvironmentExporter.Write(context)));
                files.Add((dir + PostProcessingExporter.FileName, PostProcessingExporter.Write(context)));
                files.Add((dir + TerrainExporter.FileName, TerrainExporter.Write(context)));
                files.Add((dir + GrassExporter.FileName, GrassExporter.Write(context)));
                files.Add((dir + ObjectsExporter.FileName, ObjectsExporter.Write(context)));
                files.Add((dir + WaterExporter.FileName, WaterExporter.Write(context)));
                files.Add((dir + SoundsExporter.FileName, SoundsExporter.Write(context)));

                registered.Add((scene.Name, ns));
                objects += context.Objects;
                terrains += context.Terrains;
                waters += context.Waters;
                sounds += context.Sounds;
            }

            var uiContext = new ExportContext(_scenes[0], _paths, _ui, assets, warnings);
            files.Add((UiExporter.FileName, UiExporter.Write(uiContext)));
            files.Insert(0, ("GameGenerator.cs", WriteEntryPoint(registered)));

            foreach (var (path, text) in files)
            {
                string full = Path.Combine(staging, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, text, new UTF8Encoding(false));
            }

            SwapIn(staging, folder);

            return new GenerateResult(folder, files.Select(f => f.path).ToList(), _scenes.Count, objects, terrains, waters,
                uiContext.UiElements, sounds, assets.Copied, warnings);
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    /// <summary>
    /// Replaces <paramref name="folder"/> with the finished <paramref name="staging"/> folder. The old folder is moved
    /// aside first and only deleted once the new one is in place (it is put back if that fails), so a failure never
    /// leaves the project without its generated game. Moves are retried: a virus scanner or file indexer looking at
    /// the freshly written files can block them for a moment.
    /// </summary>
    private static void SwapIn(string staging, string folder)
    {
        string old = folder + ".old";
        if (Directory.Exists(old)) Directory.Delete(old, recursive: true);
        if (Directory.Exists(folder)) MoveWithRetry(folder, old);
        try
        {
            MoveWithRetry(staging, folder);
        }
        catch
        {
            if (Directory.Exists(old) && !Directory.Exists(folder)) MoveWithRetry(old, folder);
            throw;
        }
        try { Directory.Delete(old, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* left for the next Compile */ }
    }

    private static void MoveWithRetry(string from, string to)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(from, to);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 20)
            {
                Thread.Sleep(150);
            }
        }
    }

    /// <summary>A scene's SceneGenerator.cs: fills a scene by calling the scene's other generators in order.</summary>
    private static string WriteSceneEntry(Scene scene, string ns)
    {
        var w = new CodeWriter($"The scene '{scene.Name}': builds it by calling the other generators of this folder in order.", ns);
        w.Line($"/// <summary>Fills a new scene with everything of '{System.Security.SecurityElement.Escape(scene.Name)}'.</summary>");
        w.Open("public static class SceneGenerator");
        w.Open("public static void Build(Scene scene)");
        w.Line("EnvironmentGenerator.Build(scene);");
        w.Line("PostProcessingGenerator.Build(scene);");
        w.Line("TerrainGenerator.Build(scene);");
        w.Line("GrassGenerator.Build(scene);");
        w.Line("ObjectsGenerator.Build(scene);");
        w.Line("WaterGenerator.Build(scene);");
        w.Line("SoundsGenerator.Build(scene);   // after the objects, so sounds can follow them");
        w.Close();
        w.Close();
        return w.ToString();
    }

    private static string WriteEntryPoint(IReadOnlyList<(string name, string ns)> scenes)
    {
        var w = new CodeWriter("The entry point of the generated game: the game UI, then every scene, starting with the first.");
        w.Line("/// <summary>");
        w.Line("/// Builds the whole game the editor saved. Call <c>GameGenerator.Build(window)</c> instead of building a scene by hand.");
        w.Line("/// Scripts switch scenes with <c>Game.LoadScene(\"Name\")</c>.");
        w.Line("/// </summary>");
        w.Open("public static class GameGenerator");
        w.Line("/// <summary>True once the editor has generated a game (a project without one has an empty GameGenerator).</summary>");
        w.Line("public static readonly bool IsGenerated = true;");
        w.Line();
        w.Line("/// <summary>The scenes, in order: the game starts with the first one.</summary>");
        w.Line($"public static readonly string[] SceneNames = {{ {string.Join(", ", scenes.Select(s => Str(s.name)))} }};");
        w.Line();
        w.Open("public static void Build(Window window)");
        w.Line("UiGenerator.Build(window.GameUi);");
        foreach (var (name, ns) in scenes)
            w.Line($"window.Scenes.Register({Str(name)}, global::{ns}.SceneGenerator.Build);");
        w.Line($"window.Scenes.LoadNow({Str(scenes[0].name)});");
        w.Close();
        w.Close();
        return w.ToString();
    }
}
