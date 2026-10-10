using GT67.Utils;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GT67.Core;

/// <summary>Container for root objects, lights and the skybox.</summary>
public sealed class Scene : IDisposable
{
    public const int MaxPointLights = 8;

    private readonly List<GameObject> _roots = new();
    private readonly List<GameObject> _updateBuffer = new();
    private readonly List<GameObject> _keyBuffer = new();
    private readonly List<Water> _waters = new();
    private readonly List<Terrain> _terrains = new();
    private readonly List<Sound> _sounds = new();

    public IReadOnlyList<GameObject> Roots => _roots;

    /// <summary>Editable terrains in the scene (each is also a normal object in <see cref="Roots"/>).</summary>
    public IReadOnlyList<Terrain> Terrains => _terrains;

    /// <summary>Sounds placed in the scene (saved with the map; see <see cref="Sound"/>).</summary>
    public IReadOnlyList<Sound> Sounds => _sounds;

    /// <summary>Bodies of water (lakes, ponds, sea) in the scene.</summary>
    public IReadOnlyList<Water> Waters => _waters;

    public Sun? Sun { get; set; }
    public List<PointLight> PointLights { get; } = new();
    public SpotLight? Spot { get; set; }

    public Color4 AmbientColor { get; set; } = new(0.6f, 0.7f, 0.9f, 1f);
    public float AmbientIntensity { get; set; } = 0.15f;

    public Skybox? Skybox { get; set; }

    /// <summary>The scripting API, set by the window that runs this scene.</summary>
    public Scripting.Game? Game { get; internal set; }

    /// <summary>
    /// False pauses the world: objects stop updating and scripts and key callbacks do not run (the editor uses this
    /// while you are editing). The sun, sky and water keep animating.
    /// </summary>
    public bool Simulate { get; set; } = true;

    // ---- weather ---------------------------------------------------------------------------------

    /// <summary>The rain (null = dry): <c>scene.Rain = new Rain { Intensity = 0.7f };</c> Drives the storm sky, fog, wet ground and puddles.</summary>
    public Rain? Rain { get; set; }

    /// <summary>How hard it rains right now, 0..1 (from <see cref="Rain"/>).</summary>
    public float RainAmount => Rain?.Amount ?? 0f;

    /// <summary>How wet the surfaces are, 0..1. Rises while it rains and slowly dries afterwards.</summary>
    public float Wetness { get; private set; }

    /// <summary>How stormy the sky is, 0..1. Follows <see cref="RainAmount"/> with a delay, so the clouds darken before the rain starts.</summary>
    public float Storm { get; private set; }

    /// <summary>Base fog density per metre (0 = clear air; 0.002 is a light haze). Rain thickens it further.</summary>
    public float Fog { get; set; }

    /// <summary>Fog density per metre actually used: <see cref="Fog"/> plus the storm's mist.</summary>
    public float FogDensity => Fog + Storm * 0.005f;

    /// <summary>World height of the flat ground where puddles mirror the scene (they reflect the world around this level).</summary>
    public float PuddleLevel { get; set; }

    /// <summary>How much the storm dims the sun and sky light, as a multiplier (1 = clear).</summary>
    public float LightDim => 1f - 0.9f * Storm;
    public float AmbientDim => 1f - 0.7f * Storm;

    /// <summary>Colour of the fog (linear): the sky's ambient light, greyed and darkened as the storm builds.</summary>
    public Vector3 FogColor
    {
        get
        {
            var sky = MathUtil.ToLinear(AmbientColor) * AmbientIntensity * 2.2f * AmbientDim;
            float lum = sky.X * 0.2126f + sky.Y * 0.7152f + sky.Z * 0.0722f;
            return Vector3.Lerp(sky, new Vector3(lum), 0.5f * Storm);
        }
    }

    /// <summary>Set (with a timestamp) by the volumetric fog effect while it runs; the shaders then skip their simple fog.</summary>
    internal long VolumetricFogTick { get; set; }
    internal bool VolumetricFogActive => Environment.TickCount64 - VolumetricFogTick < 300;

    /// <summary>Seconds of weather animation (rain ripples on wet ground).</summary>
    public float WeatherTime { get; private set; }

    // ---- objects ---------------------------------------------------------------------------------

    public void Add(params GameObject[] objects)
    {
        foreach (var obj in objects)
        {
            if (obj.IsDestroyed) { Log.Warn($"Ignoring destroyed object '{obj.Name}'."); continue; }
            if (obj.Parent != null) { Log.Warn($"'{obj.Name}' has a parent; add the parent instead."); continue; }
            if (_roots.Contains(obj)) continue;

            _roots.Add(obj);
            SetScene(obj, this);
        }
    }

    /// <summary>Adds terrains (their objects become scene roots). Destroying the terrain object removes it again.</summary>
    public void Add(params Terrain[] terrains)
    {
        foreach (var terrain in terrains)
        {
            if (_terrains.Contains(terrain)) continue;
            _terrains.Add(terrain);
            Add(terrain.Object);
        }
    }

    public void Remove(params Terrain[] terrains)
    {
        foreach (var terrain in terrains)
        {
            _terrains.Remove(terrain);
            RemoveRoot(terrain.Object);
        }
    }

    /// <summary>Adds bodies of water. The scene disposes them with <see cref="Dispose"/> or <see cref="Clear"/>.</summary>
    public void Add(params Water[] waters)
    {
        foreach (var water in waters)
            if (!_waters.Contains(water)) _waters.Add(water);
    }

    /// <summary>Removes bodies of water from the scene without disposing them.</summary>
    public void Remove(params Water[] waters)
    {
        foreach (var water in waters) _waters.Remove(water);
    }

    /// <summary>Detaches objects from the scene without destroying them.</summary>
    public void Remove(params GameObject[] objects)
    {
        foreach (var obj in objects) RemoveRoot(obj);
    }

    internal void RemoveRoot(GameObject obj)
    {
        if (_roots.Remove(obj)) SetScene(obj, null);
    }

    internal static void SetScene(GameObject obj, Scene? scene)
    {
        obj.Scene = scene;
        foreach (var child in obj.Children) SetScene(child, scene);
    }

    /// <summary>Destroys every object (releasing GPU resources) and empties the scene.</summary>
    public void Clear()
    {
        foreach (var obj in _roots.ToArray()) obj.Destroy();
        _roots.Clear();
        _terrains.Clear();
        foreach (var water in _waters) water.Dispose();
        _waters.Clear();
        foreach (var sound in _sounds) sound.Stop();
        _sounds.Clear();
    }

    public void AddLight(PointLight light)
    {
        if (PointLights.Count >= MaxPointLights)
        {
            Log.Warn($"Only {MaxPointLights} point lights are supported; '{light.Name}' ignored.");
            return;
        }
        PointLights.Add(light);
    }

    // ---- queries ---------------------------------------------------------------------------------

    public IEnumerable<GameObject> Traverse()
    {
        foreach (var root in _roots.ToArray())
            foreach (var obj in Descendants(root))
                yield return obj;
    }

    private static IEnumerable<GameObject> Descendants(GameObject obj)
    {
        yield return obj;
        foreach (var child in obj.Children)
            foreach (var d in Descendants(child))
                yield return d;
    }

    /// <summary>Fills <paramref name="buffer"/> with every enabled object (disabled subtrees are skipped).</summary>
    public void CollectEnabled(List<GameObject> buffer)
    {
        buffer.Clear();
        foreach (var root in _roots) Collect(root, buffer);
    }

    private static void Collect(GameObject obj, List<GameObject> buffer)
    {
        if (!obj.Enabled) return;
        buffer.Add(obj);
        foreach (var child in obj.Children) Collect(child, buffer);
    }

    /// <summary>Adds sounds to the scene: they follow their object, start with the game when <see cref="Sound.PlayOnStart"/> is set, and are written by Compile.</summary>
    public void Add(params Sound[] sounds)
    {
        foreach (var sound in sounds)
            if (!_sounds.Contains(sound)) _sounds.Add(sound);
    }

    public void Remove(params Sound[] sounds)
    {
        foreach (var sound in sounds)
        {
            sound.Stop();
            _sounds.Remove(sound);
        }
    }

    public Sound? FindSound(string name) => _sounds.FirstOrDefault(s => s.Name == name);

    public GameObject? Find(string name) => Traverse().FirstOrDefault(o => o.Name == name);

    public List<GameObject> FindByTag(string tag) => Traverse().Where(o => o.Tag == tag).ToList();

    public GameObject? FindById(Guid id) => Traverse().FirstOrDefault(o => o.Id == id);

    public GameObject? Raycast(Vector3 origin, Vector3 direction) => Raycast(origin, direction, out _);

    /// <summary>Closest object hit by the ray (triangle accurate), or null.</summary>
    public GameObject? Raycast(Vector3 origin, Vector3 direction, out float distance)
    {
        var ray = new Ray(origin, direction);
        GameObject? closest = null;
        distance = float.MaxValue;

        foreach (var obj in Traverse())
        {
            if (obj.Raycast(ray, out float t) && t < distance)
            {
                distance = t;
                closest = obj;
            }
        }
        return closest;
    }

    // ---- collisions ------------------------------------------------------------------------------

    /// <summary>
    /// Moves a sphere (centre <paramref name="position"/>, <paramref name="radius"/>) by <paramref name="delta"/>,
    /// sliding along every solid object (<see cref="GameObject.Collision"/>) except <paramref name="ignore"/>.
    /// <paramref name="onContact"/> is called after the move for each object that pushed the sphere.
    /// </summary>
    public MoveResult MoveAndSlide(Vector3 position, Vector3 delta, float radius, GameObject? ignore = null,
        Action<GameObject, Vector3>? onContact = null)
    {
        var objects = new List<GameObject>();
        CollectEnabled(objects);
        return Collision.MoveSphere(objects, position, delta, radius, ignore, onContact);
    }

    /// <summary>Every solid object whose collider the sphere overlaps (cheap: bounding boxes only).</summary>
    public List<GameObject> OverlapSphere(Vector3 center, float radius)
    {
        var box = new Aabb(center - new Vector3(radius), center + new Vector3(radius));
        var objects = new List<GameObject>();
        CollectEnabled(objects);
        objects.RemoveAll(o => o.Collision == CollisionMode.None || !o.Bounds.Intersects(box));
        return objects;
    }

    // ---- key callbacks ---------------------------------------------------------------------------

    /// <summary>True if any enabled object has a callback for <paramref name="action"/>.</summary>
    public bool HasKeyCallbacks(KeyAction action)
    {
        if (!Simulate) return false;
        CollectEnabled(_keyBuffer);
        foreach (var obj in _keyBuffer)
            if (obj.HasKeyHandler(action)) return true;
        return false;
    }

    /// <summary>
    /// Runs every enabled object's callback for <paramref name="action"/> (children included).
    /// Objects created or destroyed by a callback take effect from the next dispatch.
    /// </summary>
    public void DispatchKey(KeyAction action, Keys key, Vector3 mouseWorldPosition, Vector3 cameraPosition,
        float deltaTime)
    {
        if (!Simulate) return;
        CollectEnabled(_keyBuffer);
        for (int i = 0; i < _keyBuffer.Count; i++)
        {
            var obj = _keyBuffer[i];
            if (obj.IsDestroyed) continue;
            obj.RaiseKey(action, key, mouseWorldPosition, cameraPosition, deltaTime);
        }
    }

    // ---- frame -----------------------------------------------------------------------------------

    public void Update(float deltaTime)
    {
        WeatherTime += deltaTime;
        Rain?.Update(deltaTime);
        float stormTarget = Math.Min(1f, RainAmount * 1.5f);   // a moderate rain already closes the sky completely
        Storm = MoveToward(Storm, stormTarget, (stormTarget > Storm ? 0.35f : 0.12f) * deltaTime);
        float wetTarget = Math.Min(1f, RainAmount * 1.5f);
        Wetness = MoveToward(Wetness, wetTarget, (wetTarget > Wetness ? 0.08f : 0.03f) * deltaTime);
        if (Skybox?.Physical?.Clouds is { } clouds) clouds.Storminess = Storm;

        for (int i = 0; i < _sounds.Count; i++) _sounds[i].Update(this);
        Sun?.Update(this, deltaTime);
        Skybox?.Physical?.Update(deltaTime);
        foreach (var water in _waters) water.Update(deltaTime);

        _updateBuffer.Clear();
        _updateBuffer.AddRange(_roots);
        foreach (var obj in _updateBuffer) obj.Update(deltaTime);
        foreach (var obj in _updateBuffer) obj.LateUpdate(deltaTime); // cameras follow where things ended up

        // Things that move (driven by a script or OnUpdate) splash into water and leave wakes behind them.
        if (_waters.Count > 0 && Simulate)
        {
            _moverBuffer.Clear();
            foreach (var obj in Traverse())
                if (obj.Enabled && !obj.EditorOnly && (obj.Scripts.Count > 0 || obj.OnUpdate != null) && obj.Model.Parts.Count > 0)
                    _moverBuffer.Add(obj);
            foreach (var water in _waters) if (water.Enabled) water.TrackMovers(_moverBuffer, deltaTime);
        }
    }

    private readonly List<GameObject> _moverBuffer = new();

    /// <summary>The water whose surface covers (x, z) of <paramref name="position"/> (the highest one if several do), or null.</summary>
    public Water? WaterAt(Vector3 position)
    {
        Water? found = null;
        foreach (var water in _waters)
            if (water.Enabled && water.Contains(position.X, position.Z) && (found == null || water.Level > found.Level))
                found = water;
        return found;
    }

    private static float MoveToward(float value, float target, float maxStep) =>
        value + Math.Clamp(target - value, -maxStep, maxStep);

    public void Dispose()
    {
        Clear();
        Skybox?.Dispose();
        Skybox = null;
    }
}
