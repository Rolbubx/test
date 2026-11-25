using System.Reflection;
using System.Runtime.Loader;
using SolciteEngine.Core;
using SolciteEngine.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SolciteEngine.Editor;

/// <summary>
/// Opens the map a project saved with Compile. The generated game (<c>Generated/GeneratedGame/**/*.cs</c>) is compiled
/// together with the project's scripts in memory and its <c>GameGenerator.Build</c> is called, so a project of its own
/// reopens exactly as it was left (the engine sandbox has its generated map compiled in).
/// </summary>
public static class ProjectLoader
{
    private const string ImplicitUsings =
        "global using System; global using System.Collections.Generic; global using System.IO; " +
        "global using System.Linq; global using System.Threading; global using System.Threading.Tasks;";

    /// <summary>True if the project has a compiled game that <see cref="TryBuild"/> can load.</summary>
    public static bool HasGeneratedGame(ProjectPaths project) => File.Exists(project.GeneratedGameFile);

    /// <summary>Builds the project's saved game into the window (its scenes and UI). False (with a log line) if there is none or it does not compile.</summary>
    public static bool TryBuild(Window window, ProjectPaths project)
    {
        if (!HasGeneratedGame(project)) return false;
        try
        {
            var files = new List<string>();
            if (Directory.Exists(project.Scripts))
                files.AddRange(Directory.EnumerateFiles(project.Scripts, "*.cs", SearchOption.AllDirectories));
            files.AddRange(Directory.EnumerateFiles(project.GeneratedGame, "*.cs", SearchOption.AllDirectories)
                .Where(f => !IsBuildOutput(project.GeneratedGame, f)));

            var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(ImplicitUsings, path: "<implicit usings>") };
            // STANDALONE_GAME is not defined: the game's own Main stays out of the editor.
            foreach (var file in files) trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file));

            var compilation = CSharpCompilation.Create(
                "SolciteEngine.ProjectGame",
                trees,
                References(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug,
                    nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));

            using var dll = new MemoryStream();
            var emit = compilation.Emit(dll);
            if (!emit.Success)
            {
                foreach (var d in emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(8))
                    Log.Warn($"Could not open the saved game: {d.GetMessage()} ({d.Location.GetLineSpan().Path} {d.Location.GetLineSpan().StartLinePosition.Line + 1})");
                return false;
            }

            var context = new AssemblyLoadContext("project-game", isCollectible: false);
            dll.Position = 0;
            var assembly = context.LoadFromStream(dll);
            var generator = assembly.GetType("SolciteEngine.Generated.GameGenerator");
            var build = generator?.GetMethod("Build", BindingFlags.Public | BindingFlags.Static);
            if (build == null)
            {
                Log.Warn("The saved game has no GameGenerator.Build.");
                return false;
            }

            Log.Info($"Opening the saved game of '{project.Name}'.");
            build.Invoke(null, new object[] { window });
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TargetInvocationException)
        {
            Log.Warn($"Could not open the saved game: {(e as TargetInvocationException)?.InnerException?.Message ?? e.Message}");
            window.Scenes.Clear();   // the scenes it registered before failing must not be built again by the editor
            return false;
        }
    }

    private static bool IsBuildOutput(string root, string file)
    {
        string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        return relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<MetadataReference>? _references;

    private static IReadOnlyList<MetadataReference> References()
    {
        if (_references != null) return _references;
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => Path.GetFileName(p) is { } name &&
                        !name.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase) &&
                        !name.StartsWith("System.Reflection.Metadata", StringComparison.OrdinalIgnoreCase));
        var list = new List<MetadataReference>();
        foreach (var path in paths)
        {
            try { list.Add(MetadataReference.CreateFromFile(path)); }
            catch (Exception e) when (e is IOException or BadImageFormatException) { /* native file: not referenced */ }
        }
        return _references = list;
    }
}
