using GT67.Core;
using GT67.Scripting;
using GT67.Utils;

namespace GT67.Editor;

/// <summary>
/// The editor's view of <c>Scripts/*.cs</c>: lists the files, creates new ones from a template, compiles them live
/// with Roslyn and re-attaches the new versions of the scripts to the objects that use them.
/// </summary>
public sealed class ScriptWorkspace : IDisposable
{
    private const string Template = """
        /// <summary>Describe what this script does.</summary>
        public class {NAME} : Script
        {
            // Public fields of simple types (float, int, bool, Vector3, Color4, enums) appear in the editor.
            public float Speed = 1f;

            public override void OnStart()
            {
            }

            public override void OnUpdate(float deltaTime)
            {
                // Self is the object this script is attached to; Game gives time, input, Find(...) and Spawn(...).
            }
        }
        """;

    private readonly ScriptCompiler _compiler = new();
    private readonly ProjectPaths? _paths;

    private FileSystemWatcher? _watcher;
    private long _changedAtTicks;

    public ScriptWorkspace(ProjectPaths? paths)
    {
        _paths = paths;
        StartWatching();
    }

    // ---- watching the folder ---------------------------------------------------------------------

    private void StartWatching()
    {
        if (_paths == null) return;
        try
        {
            Directory.CreateDirectory(_paths.Scripts);
            _watcher = new FileSystemWatcher(_paths.Scripts, "*.cs")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnFolderChanged;
            _watcher.Created += OnFolderChanged;
            _watcher.Deleted += OnFolderChanged;
            _watcher.Renamed += (_, _) => OnFolderChanged(null, null!);
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Log.Warn($"Not watching the Scripts folder: {e.Message}");
        }
    }

    private void OnFolderChanged(object? sender, FileSystemEventArgs? e) =>
        Interlocked.Exchange(ref _changedAtTicks, DateTime.UtcNow.Ticks);

    /// <summary>
    /// True once after a script file was saved and the folder has been quiet for <paramref name="quiet"/> (editors write
    /// a file in several steps), so the caller can recompile.
    /// </summary>
    public bool ConsumeChange(TimeSpan quiet)
    {
        long changed = Interlocked.Read(ref _changedAtTicks);
        if (changed == 0 || DateTime.UtcNow.Ticks - changed < quiet.Ticks) return false;
        return Interlocked.CompareExchange(ref _changedAtTicks, 0, changed) == changed;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    /// <summary>Script classes from the last successful compile.</summary>
    public IReadOnlyList<Type> Types { get; private set; } = Array.Empty<Type>();

    public ScriptCompileResult? LastResult { get; private set; }

    public string ScriptsFolder => _paths?.Scripts ?? "";

    public IEnumerable<string> Files =>
        _paths == null || !Directory.Exists(_paths.Scripts)
            ? Enumerable.Empty<string>()
            : Directory.EnumerateFiles(_paths.Scripts, "*.cs").OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

    /// <summary>The script file that declares <c>class &lt;typeName&gt;</c>, or null.</summary>
    public string? FindSourceFile(string typeName)
    {
        foreach (var file in Files)
        {
            try
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(file), $@"class\s+{System.Text.RegularExpressions.Regex.Escape(typeName)}"))
                    return file;
            }
            catch (IOException)
            {
                // unreadable right now (being saved): skip it
            }
        }
        return null;
    }

    /// <summary>Compiles every file in the Scripts folder. On success <see cref="Types"/> is replaced.</summary>
    public ScriptCompileResult Compile()
    {
        var files = Files.ToList();
        ScriptCompileResult result;
        try
        {
            result = _compiler.Compile(files);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            result = new ScriptCompileResult { Success = false, Errors = new[] { $"Could not read scripts: {e.Message}" } };
        }

        LastResult = result;
        if (result.Success) Types = result.ScriptTypes.OrderBy(t => t.Name).ToList();
        return result;
    }

    /// <summary>Creates Scripts/&lt;name&gt;.cs from the template. Returns the new path or null (with a reason).</summary>
    public string? CreateScript(string name, out string error)
    {
        error = "";
        name = new string(name.Where(char.IsLetterOrDigit).ToArray());
        if (_paths == null) { error = "Project folder not found."; return null; }
        if (name.Length == 0 || char.IsDigit(name[0])) { error = "Use a name that starts with a letter."; return null; }

        string file = Path.Combine(_paths.Scripts, name + ".cs");
        if (File.Exists(file)) { error = $"{name}.cs already exists."; return null; }

        Directory.CreateDirectory(_paths.Scripts);
        File.WriteAllText(file, Template.Replace("{NAME}", name));
        return file;
    }

    /// <summary>
    /// Replaces each attached script with a fresh instance of the newly compiled type of the same name, copying its
    /// settings. Scripts whose class no longer exists are left as they are. Returns how many were replaced.
    /// </summary>
    public int ReattachScripts(Scene scene)
    {
        int replaced = 0;
        foreach (var obj in scene.Traverse().ToList())
        {
            foreach (var old in obj.Scripts.ToList())
            {
                var type = Types.FirstOrDefault(t => t.FullName == old.GetType().FullName);
                if (type == null)
                {
                    Log.Warn($"Script '{old.GetType().Name}' on '{obj.Name}' no longer exists in Scripts/; kept the old version.");
                    continue;
                }
                if (type == old.GetType()) continue;

                var fresh = ReplaceScript(obj, old, type);
                replaced += fresh != null ? 1 : 0;
            }
        }
        return replaced;
    }

    /// <summary>Swaps <paramref name="old"/> for a new instance of <paramref name="type"/> at the same position in the list.</summary>
    public static Script? ReplaceScript(GameObject obj, Script old, Type type)
    {
        var fresh = (Script)Activator.CreateInstance(type)!;
        ParameterAccess.CopyValues(old, fresh);
        fresh.Enabled = old.Enabled;

        obj.RemoveScript(old);
        obj.AddScript(fresh);
        return fresh;
    }
}
