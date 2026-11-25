using System.Diagnostics;
using System.Text.Json;
using SolciteEngine.Core;
using SolciteEngine.Gui;
using SolciteEngine.Utils;
using OpenTK.Graphics.OpenGL4;

namespace SolciteEngine.Editor;

/// <summary>
/// What the editor does the first time it starts (and again after a graphics driver update): checks the graphics driver and the
/// native libraries the engine needs, compiles every engine shader once so the programs are stored in the shader cache, and
/// prepares the per-user folders. Progress is drawn as a setup screen; <c>setup.json</c> in the user data folder records that
/// it was done. <c>--setup</c> runs it again.
/// </summary>
public sealed class FirstRunSetup
{
    private const int Version = 1;
    private const double FrameBudgetMs = 40;   // shader work per frame, so the window stays responsive

    private sealed class Marker
    {
        public int Version { get; set; }
        public string Driver { get; set; } = "";
        public string Renderer { get; set; } = "";
        public string OpenGL { get; set; } = "";
        public DateTime CompletedUtc { get; set; }
        public int Shaders { get; set; }
    }

    private enum Step { Driver, Libraries, Shaders, Folders, Done }

    private sealed record Line(bool Ok, string Text);

    private readonly Window _window;
    private readonly List<Line> _log = new();
    private readonly List<(string vert, string frag, string name)> _shaders = new();
    private Step _step = Step.Driver;
    private int _shaderIndex, _failedShaders;
    private bool _problems;

    public FirstRunSetup(Window window)
    {
        _window = window;
    }

    /// <summary>True once the setup has finished (with or without problems) and the user pressed Continue (or it needed no input).</summary>
    public bool Finished { get; private set; }

    private static string MarkerFile => Path.Combine(ShaderCache.UserDataFolder, "setup.json");

    /// <summary>False the first time, after an engine update that changes the setup, and after a graphics driver change.</summary>
    public static bool IsUpToDate()
    {
        try
        {
            if (!File.Exists(MarkerFile)) return false;
            var marker = JsonSerializer.Deserialize<Marker>(File.ReadAllText(MarkerFile));
            return marker != null && marker.Version == Version && marker.Driver == ShaderCache.DriverId;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // ---- the work, a little each frame -----------------------------------------------------------

    private void Advance()
    {
        switch (_step)
        {
            case Step.Driver: CheckDriver(); _step = Step.Libraries; break;
            case Step.Libraries: CheckLibraries(); ListShaders(); _step = Step.Shaders; break;
            case Step.Shaders: CompileShaders(); break;
            case Step.Folders: PrepareFolders(); _step = Step.Done; Complete(); break;
        }
    }

    private void CheckDriver()
    {
        string version = GL.GetString(StringName.Version) ?? "?";
        string renderer = GL.GetString(StringName.Renderer) ?? "?";
        GL.GetInteger(GetPName.MajorVersion, out int major);
        GL.GetInteger(GetPName.MinorVersion, out int minor);
        bool enough = major > 4 || (major == 4 && minor >= 1);
        Add(enough, enough ? $"OpenGL {major}.{minor}: {renderer}" : $"OpenGL {major}.{minor} is too old: SolciteEngine needs 4.1 or newer ({renderer}). Update the graphics driver.");

        GL.GetInteger(GetPName.NumProgramBinaryFormats, out int formats);
        Add(formats > 0, formats > 0
            ? "Shader cache supported by the driver."
            : "The driver cannot store compiled shaders: they are compiled at every start (slower start-up, nothing else changes).", problem: false);

        GL.GetInteger(GetPName.MaxSamples, out int samples);
        Add(true, $"Anti-aliasing up to {samples}x.");
    }

    private void CheckLibraries()
    {
        Add(true, "GLFW (window and input) and Skia (interface) are loaded.");
        try
        {
            using var assimp = Silk.NET.Assimp.Assimp.GetApi();
            Add(true, "Assimp (model import) is available.");
        }
        catch (Exception e) when (e is DllNotFoundException or FileNotFoundException or TypeInitializationException or Silk.NET.Core.Loader.SymbolLoadingException)
        {
            Add(false, "Assimp (model import) was not found: models cannot be imported. " +
                       (OperatingSystem.IsLinux() ? "Install libassimp5 (for example: sudo apt install libassimp5)." : "Reinstall the editor."));
        }
    }

    private void ListShaders()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "Shaders");
        if (!Directory.Exists(folder)) { Add(false, "The Shaders folder is missing next to the editor."); return; }

        string fullscreen = Path.Combine(folder, "post.vert");
        foreach (var frag in Directory.EnumerateFiles(folder, "*.frag").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileNameWithoutExtension(frag);
            string vert = Path.Combine(folder, name + ".vert");
            _shaders.Add((File.Exists(vert) ? vert : fullscreen, frag, name));
        }
    }

    private void CompileShaders()
    {
        var watch = Stopwatch.StartNew();
        while (_shaderIndex < _shaders.Count && watch.Elapsed.TotalMilliseconds < FrameBudgetMs)
        {
            var (vert, frag, name) = _shaders[_shaderIndex++];
            try
            {
                using var shader = new Shader(vert, frag);   // compiles, or loads from the cache; either way the cache has it afterwards
            }
            catch (Exception e) when (e is InvalidOperationException or IOException)
            {
                _failedShaders++;
                Add(false, $"Shader '{name}' did not compile: {Shader.LastError?.Split('\n')[0] ?? e.Message}");
            }
        }

        if (_shaderIndex >= _shaders.Count)
        {
            int ok = _shaders.Count - _failedShaders;
            Add(_failedShaders == 0, $"{ok} of {_shaders.Count} shaders compiled and cached ({ShaderCache.Stored} new, {ShaderCache.Hits} already cached).");
            _step = Step.Folders;
        }
    }

    private void PrepareFolders()
    {
        try
        {
            Directory.CreateDirectory(ShaderCache.UserDataFolder);
            Directory.CreateDirectory(ProjectPaths.DefaultLocation());
            Add(true, $"Projects are created in {ProjectPaths.DefaultLocation()} by default.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Add(false, $"Could not create the engine folders: {e.Message}");
        }
    }

    private void Complete()
    {
        try
        {
            Directory.CreateDirectory(ShaderCache.UserDataFolder);
            File.WriteAllText(MarkerFile, JsonSerializer.Serialize(new Marker
            {
                Version = Version,
                Driver = ShaderCache.DriverId,
                Renderer = GL.GetString(StringName.Renderer) ?? "",
                OpenGL = GL.GetString(StringName.Version) ?? "",
                CompletedUtc = DateTime.UtcNow,
                Shaders = _shaders.Count - _failedShaders,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Add(false, $"Could not record the setup: {e.Message} (it will run again next time).");
        }
        Log.Info($"First-run setup done: {_log.Count} checks, {_shaders.Count - _failedShaders}/{_shaders.Count} shaders cached.");
        if (!_problems) Finished = true;   // nothing to read: go on by itself
    }

    private void Add(bool ok, string text, bool problem = true)
    {
        _log.Add(new Line(ok, text));
        if (!ok && problem) _problems = true;
        if (!ok) Log.Warn("Setup: " + text); else Log.Info("Setup: " + text);
    }

    // ---- the screen --------------------------------------------------------------------------------

    public void Draw()
    {
        if (_step != Step.Done) Advance();

        var g = _window.Ui.Gui;
        var size = _window.Viewport;
        if (g.Begin("SetupBackground", new UiRect(0f, 0f, size.Width, size.Height), WindowFlags.Fixed | WindowFlags.Panel | WindowFlags.NoScroll)) { }
        g.End();

        float width = MathF.Min(760f, size.Width - 32f), height = MathF.Min(520f, size.Height - 32f);
        var card = new UiRect((size.Width - width) * 0.5f, (size.Height - height) * 0.5f, width, height);
        if (g.Begin("SetupCard", card, WindowFlags.Fixed | WindowFlags.NoScroll))
        {
            g.Spacing(6f);
            g.Heading("Setting up SolciteEngine");
            g.TextDim("This runs the first time the editor starts, and again after a graphics driver update.");
            g.Spacing(10f);

            float fraction = _step switch
            {
                Step.Driver => 0.02f,
                Step.Libraries => 0.05f,
                Step.Shaders => 0.1f + 0.85f * (_shaders.Count == 0 ? 1f : (float)_shaderIndex / _shaders.Count),
                Step.Folders => 0.97f,
                _ => 1f,
            };
            string label = _step switch
            {
                Step.Driver => "Checking the graphics driver...",
                Step.Libraries => "Checking the libraries...",
                Step.Shaders => $"Compiling shaders ({_shaderIndex}/{_shaders.Count})...",
                Step.Folders => "Preparing folders...",
                _ => "Done",
            };
            g.ProgressBar(fraction, label);
            g.Spacing(10f);

            if (g.BeginChild("setuplog", MathF.Max(120f, height - 190f)))
                foreach (var line in _log) g.TextStatus((line.Ok ? "OK  " : "!!  ") + line.Text, error: !line.Ok);
            g.EndChild();

            if (_step == Step.Done && !Finished)
            {
                g.Spacing(8f);
                g.TextDim("Something needs your attention (above). The editor can still start.");
                if (g.Button("Continue", 200f, ButtonStyle.Primary)) Finished = true;
            }
        }
        g.End();
    }
}
