using System.Text;
using GT67.Core;
using GT67.UI;

namespace GT67.Editor.Export;

/// <summary>What <see cref="GameExporter"/> wrote.</summary>
public sealed record GenerateResult(string Folder, IReadOnlyList<string> Files, int Objects, int Terrains, int Waters,
    int UiElements, int Sounds, int CopiedFiles, IReadOnlyList<string> Warnings);

/// <summary>
/// The Compile button: turns the scene in the editor into a small game project in <c>Generated/GeneratedGame</c>,
/// split into one C# file per topic (<c>GameGenerator.cs</c> builds the game by calling the others):
/// <c>EnvironmentGenerator</c> (sun, rain, sky, lights), <c>PostProcessingGenerator</c>, <c>TerrainGenerator</c>,
/// <c>ObjectsGenerator</c>, <c>WaterGenerator</c>, <c>SoundsGenerator</c> and <c>UiGenerator</c>. Every model
/// (.fbx, .gltf, .obj, ...), texture, sound and UI image the game uses is copied into <c>GeneratedGame/Assets</c>,
/// which the build ships to <c>Assets/Game</c> next to the executable.
/// </summary>
public sealed class GameExporter
{
    private readonly Scene _scene;
    private readonly ProjectPaths _paths;
    private readonly UiCanvas _ui;
    private readonly PostProcess _post;

    public GameExporter(Scene scene, ProjectPaths paths, UiCanvas ui, PostProcess post)
    {
        _scene = scene;
        _paths = paths;
        _ui = ui;
        _post = post;
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
            var context = new ExportContext(_scene, _paths, _ui, _post, assets, warnings);

            var files = new List<(string name, string text)>
            {
                (EnvironmentExporter.FileName, EnvironmentExporter.Write(context)),
                (PostProcessingExporter.FileName, PostProcessingExporter.Write(context)),
                (TerrainExporter.FileName, TerrainExporter.Write(context)),
                (ObjectsExporter.FileName, ObjectsExporter.Write(context)),
                (WaterExporter.FileName, WaterExporter.Write(context)),
                (SoundsExporter.FileName, SoundsExporter.Write(context)),
                (UiExporter.FileName, UiExporter.Write(context)),
            };
            files.Insert(0, ("GameGenerator.cs", WriteEntryPoint()));

            foreach (var (name, text) in files)
                File.WriteAllText(Path.Combine(staging, name), text, new UTF8Encoding(false));

            // Swap the finished folder in
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            Directory.Move(staging, folder);

            return new GenerateResult(folder, files.Select(f => f.name).ToList(), context.Objects, context.Terrains, context.Waters,
                context.UiElements, context.Sounds, assets.Copied, warnings);
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    private static string WriteEntryPoint()
    {
        var w = new CodeWriter("The entry point of the generated game: builds it by calling the other generators in order.");
        w.Line("/// <summary>");
        w.Line("/// Builds the whole game the editor saved. Call <c>GameGenerator.Build(window)</c> instead of building a scene by hand.");
        w.Line("/// </summary>");
        w.Open("public static class GameGenerator");
        w.Line("/// <summary>True once the editor has generated a game (a project without one has an empty GameGenerator).</summary>");
        w.Line("public static readonly bool IsGenerated = true;");
        w.Line();
        w.Open("public static void Build(Window window)");
        w.Line("var scene = window.Scene;");
        w.Line("EnvironmentGenerator.Build(scene);");
        w.Line("PostProcessingGenerator.Build(window.Renderer.Post);");
        w.Line("TerrainGenerator.Build(scene);");
        w.Line("ObjectsGenerator.Build(scene);");
        w.Line("WaterGenerator.Build(scene);");
        w.Line("SoundsGenerator.Build(scene);   // after the objects, so sounds can follow them");
        w.Line("UiGenerator.Build(window.GameUi);");
        w.Close();
        w.Close();
        return w.ToString();
    }
}
