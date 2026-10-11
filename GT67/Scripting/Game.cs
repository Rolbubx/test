using GT67.Core;
using GT67.Utils;
using OpenTK.Mathematics;
using CursorState = OpenTK.Windowing.Common.CursorState;

namespace GT67.Scripting;

/// <summary>The small API scripts use to talk to the game: time, input, finding and spawning objects, timers, weather.</summary>
public sealed class Game
{
    private readonly Window _window;
    private int _mouseFrame = -1, _mouseHitFrame = -1;
    private Vector3 _mouseWorld;
    private RaycastHit? _mouseHit;
    private readonly List<GameTimer> _timers = new();
    private readonly List<GameTimer> _newTimers = new();
    private readonly List<PointLight> _addedLights = new();
    private bool? _freeCameraBefore;
    private float _timeScale = 1f;

    internal Game(Window window) => _window = window;

    public Scene Scene => _window.Scene;
    public Camera Camera => _window.Camera;

    // ---- scenes ----------------------------------------------------------------------------------

    /// <summary>The name of the scene being played.</summary>
    public string SceneName => _window.Scene.Name;

    /// <summary>The names of the game's scenes (made in the editor's Hierarchy), the first one being where the game starts.</summary>
    public IReadOnlyList<string> SceneNames => _window.Scenes.Names;

    /// <summary>
    /// Switches to another scene at the end of the frame, like Unity's SceneManager.LoadScene: the current scene (its
    /// objects, scripts, sounds) is thrown away and a fresh copy of the new one starts, with its own post-processing.
    /// </summary>
    public void LoadScene(string name) => _window.Scenes.LoadScene(name);

    /// <summary>
    /// The post-processing of the scene being played, including its screen shaders:
    /// <c>Game.Volume.AddScreenShader("PS1")</c>, <c>Game.Volume.GetScreenShader("CRT")?.Set("Curvature", 0.2f)</c>.
    /// </summary>
    public PostProfile Volume => _window.Scene.Volume;

    /// <summary>Starts the current scene again from the beginning.</summary>
    public void ReloadScene() => _window.Scenes.LoadScene(_window.Scene.Name);

    /// <summary>The game's user interface (HUD, menus): find elements by name and change their text, value or visibility.</summary>
    public UI.UiCanvas Ui => _window.GameUi;

    // ---- time ------------------------------------------------------------------------------------

    /// <summary>Game seconds since the window opened (they follow <see cref="TimeScale"/>).</summary>
    public float Time { get; internal set; }

    /// <summary>Game seconds the last frame took (real seconds times <see cref="TimeScale"/>).</summary>
    public float DeltaTime { get; internal set; }

    /// <summary>Real seconds the last frame took, ignoring <see cref="TimeScale"/> (for menus while the game is paused).</summary>
    public float UnscaledDeltaTime { get; internal set; }

    /// <summary>
    /// How fast the game runs: 1 = normal, 0.5 = slow motion, 0 = paused (scripts still get OnUpdate, with a delta of 0).
    /// Reset to 1 when the editor stops playing.
    /// </summary>
    public float TimeScale
    {
        get => _timeScale;
        set => _timeScale = Math.Max(0f, value);
    }

    /// <summary>True while <see cref="TimeScale"/> is 0. Setting it pauses the game or resumes it at normal speed.</summary>
    public bool Paused
    {
        get => _timeScale == 0f;
        set => _timeScale = value ? 0f : 1f;
    }

    internal int Frame { get; set; }

    // ---- input -----------------------------------------------------------------------------------

    public bool IsKeyDown(Keys key) => _window.KeyboardState.IsKeyDown(key);

    /// <summary>True only on the frame the key went down.</summary>
    public bool WasKeyPressed(Keys key) => _window.KeyboardState.IsKeyPressed(key);

    /// <summary>True only on the frame the key was released.</summary>
    public bool WasKeyReleased(Keys key) => _window.KeyboardState.IsKeyReleased(key);

    public bool IsMouseDown(MouseButton button) => _window.MouseState.IsButtonDown(button);

    /// <summary>True only on the frame the mouse button went down (a click).</summary>
    public bool WasMousePressed(MouseButton button) => _window.MouseState.IsButtonPressed(button);

    /// <summary>True only on the frame the mouse button was released.</summary>
    public bool WasMouseReleased(MouseButton button) => _window.MouseState.IsButtonReleased(button);

    /// <summary>The cursor in the game view, in points from its top-left corner.</summary>
    public Vector2 MousePosition => _window.MouseInViewport;

    /// <summary>How far the mouse moved since the last frame, in points.</summary>
    public Vector2 MouseDelta => _window.MouseState.Delta;

    /// <summary>Mouse wheel movement this frame (positive = wheel pushed away from you).</summary>
    public float MouseScroll => _window.MouseState.ScrollDelta.Y;

    /// <summary>True when the cursor is over a UI panel or button, so a click there is probably not meant for the world.</summary>
    public bool IsMouseOverUi => _window.Ui.Gui.IsPointOverUi(_window.MousePosition);

    /// <summary>
    /// Captures the cursor (hidden and locked, like F1): moving the mouse turns the camera and the mouse world
    /// position is the screen centre. Set it back to false to release the cursor. Released when the editor stops playing.
    /// </summary>
    public bool CursorLocked
    {
        get => _window.CursorState == CursorState.Grabbed;
        set
        {
            if (value != CursorLocked) _window.ToggleMouseCapture();
        }
    }

    /// <summary>
    /// True (the default) lets WASD/QE fly the camera. Set it to false when your scripts use those keys or move the
    /// camera themselves. Restored when the editor stops playing.
    /// </summary>
    public bool FreeCamera
    {
        get => _window.FreeCamera;
        set
        {
            _freeCameraBefore ??= _window.FreeCamera;
            _window.FreeCamera = value;
        }
    }

    /// <summary>
    /// True while the editor's user flies around freely during Play ("Free view" in the toolbar): camera scripts
    /// should leave the camera alone. Always false in the built game.
    /// </summary>
    public bool ViewDetached { get; internal set; }

    /// <summary>The world point under the mouse cursor (scene hit, else the ground plane). Cached per frame.</summary>
    public Vector3 MouseWorldPosition
    {
        get
        {
            if (_mouseFrame != Frame)
            {
                _mouseWorld = _window.GetMouseWorldPosition();
                _mouseFrame = Frame;
            }
            return _mouseWorld;
        }
    }

    /// <summary>The object under the mouse cursor and the point where the cursor touches it (null over empty sky). Cached per frame.</summary>
    public RaycastHit? MouseHit
    {
        get
        {
            if (_mouseHitFrame != Frame)
            {
                var ray = _window.GetMouseRay();
                _mouseHit = Raycast(ray.Origin, ray.Direction);
                _mouseHitFrame = Frame;
            }
            return _mouseHit;
        }
    }

    // ---- scene helpers ---------------------------------------------------------------------------

    /// <summary>Plays a sound file once and forgets about it. <paramref name="at"/> makes it a 3D sound.</summary>
    public Sound? PlaySound(string file, float volume = 1f, Vector3? at = null, float pitch = 1f) => Sound.PlayOnce(file, volume, at, pitch);

    /// <summary>A sound placed in the scene, by name.</summary>
    public Sound? FindSound(string name) => Scene.FindSound(name);

    // ---- weather and light -----------------------------------------------------------------------

    /// <summary>The rain of the scene (null = dry). Change its settings while the game runs.</summary>
    public Rain? Rain
    {
        get => Scene.Rain;
        set => Scene.Rain = value;
    }

    /// <summary>
    /// Makes it rain: the sky closes over, fog thickens, the ground gets wet and forms puddles.
    /// <paramref name="seconds"/> is how long it lasts before it eases off and the sun comes back (0 = until <see cref="StopRain"/>).
    /// </summary>
    public Rain StartRain(float intensity = 0.7f, float seconds = 90f)
    {
        var rain = Scene.Rain ??= new Rain();
        rain.Enabled = true;
        rain.Intensity = intensity;
        rain.Duration = seconds;
        rain.Restart();
        return rain;
    }

    /// <summary>Lets the rain ease off now (the sky clears and the ground dries by itself).</summary>
    public void StopRain()
    {
        if (Scene.Rain is { } rain && rain.Amount > 0f)
        {
            rain.Duration = rain.Elapsed + rain.FadeTime;   // its normal ending, starting now
            rain.Pause = 0f;
        }
    }

    /// <summary>The sun and the time of day (null if the scene has no sun).</summary>
    public Sun? Sun => Scene.Sun;

    /// <summary>Moves the sun to a time of day, 0-24 hours (14.5 = half past two).</summary>
    public void SetTimeOfDay(float hours)
    {
        if (Scene.Sun is { } sun) sun.TimeOfDay = hours;
    }

    /// <summary>The water under (x, z) of <paramref name="position"/>, or null: ask it for <c>GetHeightAt</c>, or <c>Splash</c> it.</summary>
    public Water? WaterAt(Vector3 position) => Scene.WaterAt(position);

    /// <summary>Sends rings across the water at <paramref name="position"/> (nothing happens if there is no water there).</summary>
    public void Splash(Vector3 position, float strength = 1f)
    {
        if (Scene.WaterAt(position) is { } water) water.Splash(new Vector3(position.X, water.Level, position.Z), strength);
    }

    /// <summary>Base fog density per metre (0 = clear air, 0.002 = light haze, 0.01 = thick fog).</summary>
    public float Fog
    {
        get => Scene.Fog;
        set => Scene.Fog = value;
    }

    /// <summary>
    /// Adds a point light (a lamp, a fire, a muzzle flash). Returns null when the scene already has the maximum of 8.
    /// Lights added here are removed when the editor stops playing.
    /// </summary>
    public PointLight? AddLight(Vector3 position, Color4? color = null, float intensity = 1f, float range = 15f)
    {
        if (Scene.PointLights.Count >= Scene.MaxPointLights)
        {
            Utils.Log.Warn($"[script] Only {Scene.MaxPointLights} point lights are supported; light not added.");
            return null;
        }
        var light = new PointLight(position, color, intensity, range) { Name = "Script light" };
        Scene.AddLight(light);
        _addedLights.Add(light);
        return light;
    }

    public void RemoveLight(PointLight light)
    {
        Scene.PointLights.Remove(light);
        _addedLights.Remove(light);
    }

    // ---- finding, spawning and queries -----------------------------------------------------------

    public GameObject? Find(string name) => Scene.Find(name);

    public List<GameObject> FindByTag(string tag) => Scene.FindByTag(tag);

    /// <summary>Height of the first solid surface straight below (x, z), or <paramref name="fallback"/> if there is none.</summary>
    public float GroundHeight(float x, float z, float fallback = 0f)
    {
        var hit = Scene.Raycast(new Vector3(x, 10000f, z), -Vector3.UnitY, out float distance);
        return hit == null ? fallback : 10000f - distance;
    }

    /// <summary>The closest object along a ray, within <paramref name="maxDistance"/> (null if nothing is hit).</summary>
    public RaycastHit? Raycast(Vector3 origin, Vector3 direction, float maxDistance = float.MaxValue)
    {
        var obj = Scene.Raycast(origin, direction, out float distance);
        if (obj == null || distance > maxDistance) return null;
        return new RaycastHit(obj, new Ray(origin, direction).GetPoint(distance), distance);
    }

    /// <summary>Solid objects near a point (bounding boxes only, so cheap enough to call every frame).</summary>
    public List<GameObject> OverlapSphere(Vector3 center, float radius) => Scene.OverlapSphere(center, radius);

    /// <summary>Creates a primitive ("cube", "sphere", "cylinder", "cone", "capsule", "torus") and adds it to the scene.</summary>
    public GameObject Spawn(string primitive, Vector3 position, Color4? color = null)
    {
        Mesh mesh = primitive.ToLowerInvariant() switch
        {
            "sphere" => Primitives.Sphere(),
            "cylinder" => Primitives.Cylinder(),
            "cone" => Primitives.Cone(),
            "capsule" => Primitives.Capsule(),
            "torus" => Primitives.Torus(),
            _ => Primitives.Cube(),
        };
        var obj = new GameObject(mesh, position) { Name = primitive, Color = color ?? Color4.White };
        Scene.Add(obj);
        return obj;
    }

    /// <summary>Loads a model file (cached, so spawning the same file many times is cheap) and adds it to the scene. Null if it cannot be loaded.</summary>
    public GameObject? SpawnModel(string file, Vector3 position)
    {
        var model = Model.TryLoad(file);
        if (model == null) return null;
        var obj = new GameObject(model, position) { Name = Path.GetFileNameWithoutExtension(file) };
        Scene.Add(obj);
        return obj;
    }

    /// <summary>
    /// Adds a copy of a prefab (Prefabs/&lt;name&gt;.prefab, saved in the editor with "Save as prefab") to the scene at
    /// <paramref name="position"/>, with its children and scripts. Null (and a warning) if there is no such prefab.
    /// </summary>
    public GameObject? SpawnPrefab(string name, Vector3 position) => Prefab.Spawn(Scene, name, position);

    /// <summary>Adds a copy of <paramref name="original"/> (with its scripts and their settings) to the scene at <paramref name="position"/>.</summary>
    public GameObject Instantiate(GameObject original, Vector3 position)
    {
        var copy = original.Clone();
        copy.Position = position;
        Scene.Add(copy);
        return copy;
    }

    public void Destroy(GameObject obj) => obj.Destroy();

    /// <summary>Destroys the object <paramref name="seconds"/> from now (if it still exists then).</summary>
    public void Destroy(GameObject obj, float seconds) => After(seconds, () =>
    {
        if (!obj.IsDestroyed) obj.Destroy();
    });

    // ---- timers ----------------------------------------------------------------------------------

    /// <summary>Runs <paramref name="action"/> once, <paramref name="seconds"/> from now. Keep the timer to cancel it.</summary>
    public GameTimer After(float seconds, Action action) => AddTimer(new GameTimer(seconds, false, action));

    /// <summary>Runs <paramref name="action"/> every <paramref name="seconds"/> until the timer is cancelled.</summary>
    public GameTimer Every(float seconds, Action action) => AddTimer(new GameTimer(seconds, true, action));

    private GameTimer AddTimer(GameTimer timer)
    {
        _newTimers.Add(timer); // joins on the next tick, so starting a timer from a timer is safe
        return timer;
    }

    // ---- random ----------------------------------------------------------------------------------

    /// <summary>A random number from <paramref name="min"/> to <paramref name="max"/>.</summary>
    public float Random(float min, float max) => min + (float)System.Random.Shared.NextDouble() * (max - min);

    /// <summary>A random whole number from <paramref name="min"/> up to, but not including, <paramref name="max"/>.</summary>
    public int Random(int min, int max) => System.Random.Shared.Next(min, max);

    /// <summary>True with the given probability (0.25 = one time in four).</summary>
    public bool Chance(float probability) => System.Random.Shared.NextDouble() < probability;

    /// <summary>A random point within <paramref name="radius"/> of <paramref name="center"/> on the horizontal plane (same height as the centre).</summary>
    public Vector3 RandomPointAround(Vector3 center, float radius)
    {
        float angle = Random(0f, MathF.Tau);
        float distance = radius * MathF.Sqrt((float)System.Random.Shared.NextDouble()); // uniform over the disc
        return center + new Vector3(MathF.Cos(angle) * distance, 0f, MathF.Sin(angle) * distance);
    }

    public void Log(string message) => Utils.Log.Info($"[script] {message}");

    // ---- plumbing --------------------------------------------------------------------------------

    /// <summary>Advances the timers (only while the scene simulates).</summary>
    internal void Tick(float deltaTime)
    {
        if (!Scene.Simulate) return;
        _timers.AddRange(_newTimers);
        _newTimers.Clear();
        _timers.RemoveAll(t => !t.Tick(deltaTime));
    }

    /// <summary>Forgets what belonged to the old scene (timers, lights added by scripts) when another scene is loaded.</summary>
    internal void ResetForNewScene()
    {
        _timers.Clear();
        _newTimers.Clear();
        _addedLights.Clear();
    }

    /// <summary>Undoes what scripts changed through this API, when the editor stops playing.</summary>
    internal void Reset()
    {
        _timers.Clear();
        _newTimers.Clear();
        _timeScale = 1f;
        foreach (var light in _addedLights) Scene.PointLights.Remove(light);
        _addedLights.Clear();
        if (_freeCameraBefore is { } free) _window.FreeCamera = free;
        _freeCameraBefore = null;
        CursorLocked = false;
    }
}
