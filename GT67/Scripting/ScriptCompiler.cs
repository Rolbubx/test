using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using GT67.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GT67.Scripting;

/// <summary>
/// Compiles <c>Scripts/*.cs</c> in memory with Roslyn so scripts can be edited while the editor runs. Every compile
/// loads into its own collectible <see cref="AssemblyLoadContext"/>, so old versions are freed once nothing uses them.
/// Only the editor uses this: the game build compiles the same files normally.
/// </summary>
public sealed class ScriptCompiler
{
    // Same implicit usings the project gets from <ImplicitUsings>, which Roslyn does not add on its own.
    private const string ImplicitUsings =
        "global using System; global using System.Collections.Generic; global using System.IO; " +
        "global using System.Linq; global using System.Threading; global using System.Threading.Tasks;";

    private IReadOnlyList<MetadataReference>? _references;
    private AssemblyLoadContext? _context;
    private int _generation;

    /// <summary>Compiles the given source files into one assembly.</summary>
    public ScriptCompileResult Compile(IEnumerable<string> files)
    {
        var watch = Stopwatch.StartNew();
        var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(ImplicitUsings, path: "<implicit usings>") };
        foreach (var file in files)
            trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file));

        var compilation = CSharpCompilation.Create(
            $"GT67.Scripts.{++_generation}",
            trees,
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug,
                nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));

        using var dll = new MemoryStream();
        var emit = compilation.Emit(dll);

        var errors = emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(Format).ToList();
        var warnings = emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).Select(Format).ToList();
        if (!emit.Success)
            return new ScriptCompileResult { Success = false, Errors = errors, Warnings = warnings, Duration = watch.Elapsed };

        // Unload the previous version and load the new one in a fresh context.
        _context?.Unload();
        _context = new AssemblyLoadContext($"scripts-{_generation}", isCollectible: true);
        dll.Position = 0;
        var assembly = _context.LoadFromStream(dll);

        var types = assembly.GetTypes().Where(t => typeof(Script).IsAssignableFrom(t) && !t.IsAbstract && t.IsPublic).ToList();
        return new ScriptCompileResult
        {
            Success = true,
            Assembly = assembly,
            ScriptTypes = types,
            Warnings = warnings,
            Duration = watch.Elapsed,
        };
    }

    /// <summary>The managed assemblies the running app uses (framework, OpenTK, GT67 itself), referenced by scripts.</summary>
    private IReadOnlyList<MetadataReference> References()
    {
        if (_references != null) return _references;

        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => Path.GetFileName(p) is { } name &&
                        !name.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase) &&
                        !name.StartsWith("System.Reflection.Metadata", StringComparison.OrdinalIgnoreCase));

        var references = new List<MetadataReference>();
        foreach (var path in paths)
        {
            try
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
            catch (Exception e) when (e is IOException or BadImageFormatException)
            {
                // Native or unreadable file in the list: not something scripts can use anyway.
            }
        }
        return _references = references;
    }

    private static string Format(Diagnostic d)
    {
        var span = d.Location.GetLineSpan();
        string file = span.Path is { Length: > 0 } p ? Path.GetFileName(p) : "<script>";
        return $"{file}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): {d.Id} {d.GetMessage()}";
    }
}
