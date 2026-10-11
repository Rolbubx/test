using GT67.Utils;

namespace GT67.Core;

/// <summary>
/// The game's scenes, by name, loaded one at a time (like Unity's SceneManager). Each scene is registered with the code
/// that builds it (the editor's Compile writes that code for every scene); loading one throws the current scene away and
/// builds a fresh copy of the new one, with its own environment, objects and post-processing (<see cref="Scene.Volume"/>).
/// From a script: <c>Game.LoadScene("Level2")</c>. The switch happens at the end of the frame.
/// </summary>
public sealed class SceneManager
{
    private readonly Window _window;
    private readonly List<(string name, Action<Scene> build)> _scenes = new();
    private PostProfile? _defaults;
    private string? _pending;

    internal SceneManager(Window window) => _window = window;

    /// <summary>The names of the registered scenes, in order (the first one is where the game starts).</summary>
    public IReadOnlyList<string> Names => _scenes.Select(s => s.name).ToList();

    /// <summary>The scene being played or edited.</summary>
    public Scene ActiveScene => _window.Scene;

    /// <summary>Called after a scene was loaded (and its post-processing applied).</summary>
    public event Action<Scene>? SceneLoaded;

    /// <summary>
    /// Set by the editor while it plays: it switches between the scenes it is editing instead of building new ones.
    /// Returns true when it handled the load.
    /// </summary>
    public Func<string, bool>? LoadOverride { get; set; }

    /// <summary>Registers (or replaces) a scene and the code that fills it.</summary>
    public void Register(string name, Action<Scene> build)
    {
        int index = _scenes.FindIndex(s => Same(s.name, name));
        if (index >= 0) _scenes[index] = (name, build);
        else _scenes.Add((name, build));
    }

    public bool Exists(string name) => _scenes.Any(s => Same(s.name, name));

    /// <summary>Loads a scene at the end of the frame (scripts can call it from OnUpdate). Unknown names are logged and ignored.</summary>
    public void LoadScene(string name)
    {
        if (!Exists(name))
        {
            Log.Warn($"Scene '{name}' not found (known: {string.Join(", ", Names)}).");
            return;
        }
        _pending = name;
    }

    /// <summary>Builds a new, separate copy of a registered scene (not made active). Null if there is no such scene.</summary>
    public Scene? Build(string name)
    {
        var entry = _scenes.FirstOrDefault(s => Same(s.name, name));
        if (entry.build == null) return null;
        var scene = new Scene { Name = entry.name };
        entry.build(scene);
        return scene;
    }

    /// <summary>Loads a scene right away (at start-up, before the first frame).</summary>
    public void LoadNow(string name)
    {
        var scene = Build(name);
        if (scene == null)
        {
            Log.Warn($"Scene '{name}' not found.");
            return;
        }
        var old = _window.Scene;
        Activate(scene);
        if (old != scene) old.Dispose();
        Log.Info($"Scene '{name}' loaded.");
    }

    /// <summary>
    /// Makes <paramref name="scene"/> the one that is updated and drawn and applies its post-processing (the start-up
    /// values first, so nothing from the previous scene stays). Does not dispose the previous scene.
    /// </summary>
    public void Activate(Scene scene)
    {
        var post = _window.Renderer.Post;
        _defaults ??= PostProfile.Capture(post);
        _defaults.ApplyTo(post);
        scene.Volume.ApplyTo(post);
        _window.SetScene(scene);
        SceneLoaded?.Invoke(scene);
    }

    /// <summary>The post-processing values every scene starts from (set up in Program.cs).</summary>
    public PostProfile Defaults => _defaults ??= PostProfile.Capture(_window.Renderer.Post);

    /// <summary>Runs a load asked for during the frame.</summary>
    internal void ProcessPending()
    {
        if (_pending is not { } name) return;
        _pending = null;
        if (LoadOverride?.Invoke(name) == true) return;
        _window.Game.ResetForNewScene();
        LoadNow(name);
    }

    private static bool Same(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
}
