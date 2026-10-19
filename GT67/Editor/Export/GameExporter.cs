using System.Text;
using GT67.Core;
using GT67.UI;
using static GT67.Editor.Export.CodeWriter;

namespace GT67.Editor.Export;

/// <summary>What <see cref="GameExporter"/> wrote.</summary>
public sealed record GenerateResult(string Folder, IReadOnlyList<string> Files, int Scenes, int Objects, int Terrains, int Waters,
    int UiElements, int Sounds, int CopiedFiles, IReadOnlyList<string> Warnings);

/// <summary>
/// The Compile button: turns the editor's scenes into a small game project in <c>Generated/GeneratedGame</c>: a
/// <c>GeneratedGame.csproj</c>, one monolithic <c>Game.cs</c> and the <c>Assets</c> folder. <c>Game.cs</c> holds
/// <c>GameGenerator</c> (builds the game UI and registers every scene with the <see cref="SceneManager"/>, then loads the
/// first one), <c>UiGenerator</c> and, per scene and in its own namespace, <c>SceneGenerator</c> (calls the others),
/// <c>EnvironmentGenerator</c> (sun, rain, sky, lights), <c>PostProcessingGenerator</c> (the scene's Volume),
/// <c>TerrainGenerator</c>, <c>GrassGenerator</c>, <c>ObjectsGenerator</c>, <c>WaterGenerator</c> and <c>SoundsGenerator</c>.
/// Every model (.fbx, .gltf, .obj, ...), texture, sound and UI image the game uses is copied into
/// <c>GeneratedGame/Assets</c>; the csproj lists that folder, so a build ships it to <c>Assets/Game</c> next to the
/// executable. The csproj also builds the game on its own (<c>dotnet run --project Generated/GeneratedGame</c>).
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

            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(Path.Combine(staging, "Game.cs"), Merge(files.Select(f => f.text)), utf8);
            File.WriteAllText(Path.Combine(staging, "GeneratedGame.csproj"), ProjectFile, utf8);

            SwapIn(staging, folder);

            return new GenerateResult(folder, new[] { "GeneratedGame.csproj", "Game.cs" }, _scenes.Count, objects, terrains, waters,
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

    /// <summary>
    /// Merges the pieces (each written by a <see cref="CodeWriter"/>: banner, usings, <c>namespace X;</c>, body) into one
    /// file: one banner and one set of usings, then every piece in a block namespace, then the standalone entry point.
    /// </summary>
    private static string Merge(IEnumerable<string> pieces)
    {
        var usings = new SortedSet<string>(StringComparer.Ordinal);
        var bodies = new List<(string ns, string[] lines)>();
        foreach (var piece in pieces)
        {
            var lines = piece.Replace("\r\n", "\n").Split('\n');
            int at = Array.FindIndex(lines, l => l.StartsWith("namespace ", StringComparison.Ordinal) && l.EndsWith(';'));
            foreach (var line in lines.Take(at))
                if (line.StartsWith("using ", StringComparison.Ordinal)) usings.Add(line);
            string ns = lines[at]["namespace ".Length..^1];
            bodies.Add((ns, lines.Skip(at + 1).SkipWhile(l => l.Length == 0).ToArray()));
        }

        var sb = new StringBuilder();
        sb.Append("// <auto-generated>\n");
        sb.Append("// The generated game: the game UI, then every scene, starting with the first.\n");
        sb.Append("// Written by the GT67 editor (Compile button) on ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)).Append(".\n");
        sb.Append("// Do not edit by hand: the whole Generated/GeneratedGame folder is rewritten every time you press Compile.\n");
        sb.Append("// </auto-generated>\n#nullable enable\n");
        foreach (var u in usings) sb.Append(u).Append('\n');
        foreach (var (ns, lines) in bodies)
        {
            sb.Append("\nnamespace ").Append(ns).Append("\n{\n");
            foreach (var line in lines) sb.Append(line.Length == 0 ? "" : "    " + line).Append('\n');
            while (sb[^1] == '\n' && sb[^2] == '\n') sb.Length--;
            sb.Append("}\n");
        }
        sb.Append(StandaloneEntry.Replace("\r\n", "\n"));
        return sb.ToString();
    }

    /// <summary>Makes Game.cs a program of its own when GeneratedGame.csproj builds it (STANDALONE_GAME); inside GT67.csproj it is left out.</summary>
    private const string StandaloneEntry = """

        #if STANDALONE_GAME
        namespace GT67.Generated
        {
            /// <summary>Entry point of GeneratedGame.csproj. Options: --frames N, --scene Name, --screenshot file.png, --novsync.</summary>
            public static class GameProgram
            {
                public static int Main(string[] args)
                {
                    int frames = 0;
                    bool novsync = false;
                    string? scene = null, screenshot = null;
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (args[i] == "--frames" && i + 1 < args.Length && int.TryParse(args[i + 1], out int n)) frames = n;
                        if (args[i] == "--novsync") novsync = true;
                        if (args[i] == "--scene" && i + 1 < args.Length) scene = args[i + 1];
                        if (args[i] == "--screenshot" && i + 1 < args.Length) screenshot = args[i + 1];
                    }

                    using var window = new Window("GT67", 1280, 720, frames) { IsEditor = false, UncappedFps = novsync, ScreenshotPath = screenshot };
                    window.Initialize = w =>
                    {
                        GamePostProcessing.Setup(w, editor: false);
                        GameGenerator.Build(w);
                        if (scene != null) w.Scenes.LoadNow(scene);
                    };
                    window.Run();
                    return 0;
                }
            }
        }
        #endif

        """;

    /// <summary>
    /// GeneratedGame.csproj: builds Game.cs against the GT67 engine project and ships the assets (every model, texture,
    /// sound, mesh and shader the editor copied into Assets/) as <c>Assets/Game/...</c>, plus the engine's own shaders,
    /// fonts, skybox and prefabs, next to the executable.
    /// </summary>
    private const string ProjectFile = """
        <Project Sdk="Microsoft.NET.Sdk">

            <!-- Written by the GT67 editor (Compile button); rewritten every time you press Compile. -->
            <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
                <RollForward>Major</RollForward>
                <AssemblyName>GeneratedGame</AssemblyName>
                <RootNamespace>GT67.Generated</RootNamespace>
                <StartupObject>GT67.Generated.GameProgram</StartupObject>
                <DefineConstants>$(DefineConstants);STANDALONE_GAME</DefineConstants>
                <!-- GT67.csproj compiles Game.cs too (the editor opens the compiled map), so this project's own copy shadows that one -->
                <NoWarn>$(NoWarn);CS0436;1591</NoWarn>
            </PropertyGroup>

            <ItemGroup>
                <ProjectReference Include="..\..\GT67.csproj" />
            </ItemGroup>

            <ItemGroup>
                <!-- Models, textures, sounds, meshes, shaders and UI images of the game: the game loads them from Assets/Game -->
                <None Include="Assets\**\*.*" CopyToOutputDirectory="PreserveNewest" Link="Assets\Game\%(RecursiveDir)%(Filename)%(Extension)" />
                <!-- The engine's own files -->
                <None Include="..\..\Shaders\**\*.*" CopyToOutputDirectory="PreserveNewest" Link="Shaders\%(RecursiveDir)%(Filename)%(Extension)" />
                <None Include="..\..\Assets\Fonts\**\*.*" CopyToOutputDirectory="PreserveNewest" Link="Assets\Fonts\%(RecursiveDir)%(Filename)%(Extension)" />
                <None Include="..\..\Assets\Skybox\**\*.*" CopyToOutputDirectory="PreserveNewest" Link="Assets\Skybox\%(RecursiveDir)%(Filename)%(Extension)" />
                <None Include="..\..\Prefabs\**\*.*" CopyToOutputDirectory="PreserveNewest" Link="Prefabs\%(RecursiveDir)%(Filename)%(Extension)" />
            </ItemGroup>

        </Project>

        """;

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
