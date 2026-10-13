using GT67.Utils;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>
/// A body of water: a flat rectangle at a given level with FFT ocean waves (<see cref="OceanWaves"/>), reflections of
/// the scene and sky, refraction of what is under the surface, depth-based colour, foam and soft shores. Add it with
/// <c>scene.Add(new Water(x, level, z, width, depth))</c>; every setting is a plain property (and can also be set by
/// name through <see cref="ParameterAccess"/>), the size included. With <see cref="ReachHorizon"/> it is a sea that
/// follows the camera out to the horizon. The surface is hidden while the camera is below it.
/// It is drawn as a quadtree of tiles: small, dense tiles near the camera and ever larger ones further away (each with
/// the same grid), so even a sea of several kilometres takes only a few dozen draw calls.
/// </summary>
public sealed class Water : IDisposable
{
    /// <summary>How the second, larger sampling of the wave patch is scaled and turned (must match water.vert/.frag).</summary>
    internal const float CascadeScale = 0.37f, CascadeAngle = 0.6f, CascadeDetailWeight = 0.2f;

    private bool _disposed;
    private float _width, _depth;
    private Mesh? _grid;
    private int _gridCells;
    private readonly List<WaterNode> _nodes = new();
    private Vector2 _coverMin, _coverMax;

    /// <param name="x">Centre X.</param>
    /// <param name="level">Height of the water surface (world Y).</param>
    /// <param name="z">Centre Z.</param>
    /// <param name="width">Size along X.</param>
    /// <param name="depth">Size along Z.</param>
    /// <param name="subdivisions">Ignored (kept so maps generated before the LOD tiles still compile); see <see cref="Detail"/>.</param>
    public Water(float x, float level, float z, float width, float depth, int subdivisions = 0)
    {
        Position = new Vector3(x, level, z);
        Width = width;
        Depth = depth;
        _coverMin = new Vector2(x - Width * 0.5f, z - Depth * 0.5f);
        _coverMax = new Vector2(x + Width * 0.5f, z + Depth * 0.5f);
    }

    public string Name { get; set; } = "Water";
    public bool Enabled { get; set; } = true;

    /// <summary>Centre of the surface; Y is the water level.</summary>
    public Vector3 Position { get; set; }

    public float Level
    {
        get => Position.Y;
        set => Position = Position with { Y = value };
    }

    /// <summary>Size along X in metres (at least 0.5). Can be changed at any time.</summary>
    public float Width
    {
        get => _width;
        set => _width = MathF.Max(0.5f, value);
    }

    /// <summary>Size along Z in metres (at least 0.5). Can be changed at any time.</summary>
    public float Depth
    {
        get => _depth;
        set => _depth = MathF.Max(0.5f, value);
    }

    /// <summary>Extent of the surface in X and Z.</summary>
    public Vector2 Size => new(_width, _depth);

    /// <summary>
    /// A sea: the water ignores its rectangle and covers everything around the camera out to the end of the view; the
    /// sky below the horizon is drawn as distant sea, so there is no gap or edge. Only the level is used.
    /// </summary>
    public bool ReachHorizon { get; set; }

    // ---- level of detail -------------------------------------------------------------------------

    /// <summary>Size in metres of the smallest (nearest) tiles; each larger tile is twice the size of the one before.</summary>
    public float TileSize { get; set; } = 12f;

    /// <summary>Grid cells per tile side (every tile, near or far, has this grid; waves move per vertex).</summary>
    public int Detail { get; set; } = 64;

    /// <summary>Within this distance tiles have the smallest size; it doubles at 2x, 4x, 8x... this distance (at least 2x TileSize).</summary>
    public float LodDistance { get; set; } = 25f;

    // ---- waves -----------------------------------------------------------------------------------

    /// <summary>How high the waves are, in metres (a typical crest; the highest ones reach about twice that).</summary>
    public float WaveHeight { get; set; } = 0.07f;

    /// <summary>Length of the longest waves in metres (the FFT ocean adds every shorter wave down to a few centimetres).</summary>
    public float WaveLength { get; set; } = 5f;

    /// <summary>0 = smooth, rounded waves; 1 = sharp crests pushed sideways, with foam where they fold.</summary>
    public float Choppiness { get; set; } = 0.4f;

    /// <summary>Direction the wind blows the waves (x, z).</summary>
    public Vector2 WindDirection { get; set; } = new(1f, 0.4f);

    /// <summary>How far (degrees) the waves' directions spread around the wind: 0 = long parallel rows, 90 = confused sea.</summary>
    public float DirectionSpread { get; set; } = 50f;

    /// <summary>Animation speed multiplier (0 freezes the water).</summary>
    public float Speed { get; set; } = 1f;

    // ---- small ripples ---------------------------------------------------------------------------

    /// <summary>Strength of the fine surface ripples (they bend reflections and highlights).</summary>
    public float RippleStrength { get; set; } = 0.5f;

    /// <summary>Ripple tiling per metre (higher = finer ripples).</summary>
    public float RippleScale { get; set; } = 0.4f;

    // ---- colour ----------------------------------------------------------------------------------

    /// <summary>Colour of shallow water (sRGB).</summary>
    public Color4 ShallowColor { get; set; } = new(0.12f, 0.58f, 0.58f, 1f);

    /// <summary>Colour of deep water (sRGB).</summary>
    public Color4 DeepColor { get; set; } = new(0.02f, 0.14f, 0.24f, 1f);

    /// <summary>How fast red, green and blue light are absorbed per metre of depth (red goes first).</summary>
    public Vector3 Absorption { get; set; } = new(0.42f, 0.12f, 0.07f);

    /// <summary>How quickly shallow colour turns into deep colour with depth (per metre).</summary>
    public float DepthColorRate { get; set; } = 0.35f;

    // ---- reflection / refraction -----------------------------------------------------------------

    /// <summary>Render the scene and sky mirrored for real reflections (off = flat sky-coloured reflection).</summary>
    public bool ReflectionEnabled { get; set; } = true;

    /// <summary>Resolution of the reflection relative to the screen (0.25 to 1; higher = sharper mirror image, slower).</summary>
    public float ReflectionScale { get; set; } = 0.75f;

    /// <summary>How much ripples distort the reflection.</summary>
    public float ReflectionDistortion { get; set; } = 0.03f;

    /// <summary>How much ripples distort what is seen through the water.</summary>
    public float RefractionStrength { get; set; } = 0.05f;

    /// <summary>Reflectivity looking straight down (0.02 for real water); it rises towards grazing angles.</summary>
    public float FresnelF0 { get; set; } = 0.02f;

    // ---- sun glint -------------------------------------------------------------------------------

    public float SpecularStrength { get; set; } = 1f;
    public float Shininess { get; set; } = 300f;

    /// <summary>Tiny, very bright sun sparkles on the ripples on top of the broad glint (0 = none).</summary>
    public float Sparkle { get; set; } = 1f;

    /// <summary>Sunlight shining through the thin tops of the waves when you look towards the sun (0 = none).</summary>
    public float CrestLight { get; set; } = 1f;

    // ---- foam and shore --------------------------------------------------------------------------

    /// <summary>Amount of foam on breaking crests and along shores (0 = none).</summary>
    public float FoamAmount { get; set; } = 0.8f;

    /// <summary>How far from the shore or a submerged object the foam reaches, in metres of depth.</summary>
    public float ShoreFoamWidth { get; set; } = 0.5f;

    /// <summary>Depth over which the water fades in at the shore, in metres (0 = hard edge).</summary>
    public float EdgeSoftness { get; set; } = 0.25f;

    /// <summary>Metres over which the water fades out at the outer border of its rectangle (0 = a hard, straight edge).</summary>
    public float EdgeFade { get; set; } = 4f;

    /// <summary>Seconds of (speed-scaled) animation time.</summary>
    public float Time { get; private set; }

    /// <summary>World bounds of the surface (for a sea: the area drawn last frame), waves included.</summary>
    public Aabb Bounds
    {
        get
        {
            var min = ReachHorizon ? _coverMin : Position.Xz - Size * 0.5f;
            var max = ReachHorizon ? _coverMax : Position.Xz + Size * 0.5f;
            return new Aabb(new Vector3(min.X, Position.Y - WaveHeight * 3f, min.Y), new Vector3(max.X, Position.Y + WaveHeight * 3f, max.Y));
        }
    }

    internal Framebuffer? ReflectionBuffer { get; private set; }

    /// <summary>The FFT wave patch (created on the GL thread the first time the water is drawn).</summary>
    internal OceanWaves? Ocean { get; private set; }

    /// <summary>Advances the animation. Called by <see cref="Scene.Update"/>.</summary>
    public void Update(float deltaTime) => Time += deltaTime * Speed;

    // ---- splashes and wakes ----------------------------------------------------------------------

    /// <summary>How many ripple rings can run at once (must match MAX_SPLASHES in water.frag).</summary>
    public const int MaxSplashes = 24;

    private readonly Vector4[] _splashes = new Vector4[MaxSplashes];   // x, z, start time, strength (0 = unused)
    private int _nextSplash;

    /// <summary>Seconds a splash's rings travel before they have faded away.</summary>
    internal const float SplashLife = 4f;

    /// <summary>Brightness of the caustics: the dancing light the waves focus onto everything under the water (0 = none).</summary>
    public float Caustics { get; set; } = 1f;

    // ---- surf (a beach) -----------------------------------------------------------------------------

    /// <summary>
    /// Height of the waves rolling onto the shore, in metres (0 = none, as on a calm lake). They come in parallel to any
    /// shore, rise as the water gets shallow, break into white water and run up the sand.
    /// </summary>
    public float SurfHeight { get; set; }

    /// <summary>Seconds between two waves reaching the shore.</summary>
    public float SurfPeriod { get; set; } = 7f;

    /// <summary>Depth of water (m) where the waves start to rise; with a gentle beach slope, deeper means a wider surf zone.</summary>
    public float SurfDepth { get; set; } = 1.4f;

    /// <summary>How high (m) the water runs up the beach after each wave, as a thin sheet (0 = the shore line stays put).</summary>
    public float SwashHeight { get; set; }

    /// <summary>Height (m) of the band of wet, darker sand above the water (0 = none).</summary>
    public float WetSandHeight { get; set; } = 0.12f;

    /// <summary>Sets the look of clear tropical water on a sandy beach: surf, swash, turquoise shallows, strong caustics.</summary>
    public void ApplyBeachPreset()
    {
        SurfHeight = 0.28f;
        SurfPeriod = 7f;
        SurfDepth = 1.4f;
        SwashHeight = 0.1f;
        WetSandHeight = 0.18f;
        ShallowColor = new Color4(0.30f, 0.80f, 0.74f, 1f);
        DeepColor = new Color4(0.03f, 0.24f, 0.40f, 1f);
        Absorption = new Vector3(0.30f, 0.065f, 0.045f);
        DepthColorRate = 0.22f;
        WaveHeight = 0.1f;
        WaveLength = 6f;
        Choppiness = 0.45f;
        RippleStrength = 0.45f;
        RefractionStrength = 0.04f;
        FoamAmount = 1f;
        ShoreFoamWidth = 0.15f;
        EdgeSoftness = 0.06f;
        Caustics = 1.6f;
        CrestLight = 1.3f;
    }

    /// <summary>Strength of the rings moving things leave on the water (0 = they leave none).</summary>
    public float Interaction { get; set; } = 1f;

    /// <summary>
    /// Sends rings out across the water from <paramref name="position"/> (an object falling in, a stone, an oar).
    /// <paramref name="strength"/> around 1 is a splash, 0.2 a gentle wake. The oldest ring gives way when too many run.
    /// </summary>
    public void Splash(Vector3 position, float strength = 1f)
    {
        if (strength <= 0f) return;
        _splashes[_nextSplash] = new Vector4(position.X, position.Z, Time, MathF.Min(strength, 3f));
        _nextSplash = (_nextSplash + 1) % MaxSplashes;
    }

    /// <summary>The live splashes for the shader (finished ones have strength 0).</summary>
    internal ReadOnlySpan<Vector4> Splashes
    {
        get
        {
            for (int i = 0; i < MaxSplashes; i++)
                if (_splashes[i].W > 0f && Time - _splashes[i].Z > SplashLife) _splashes[i].W = 0f;
            return _splashes;
        }
    }

    /// <summary>True if (x, z) lies on this water (always, for a sea that reaches the horizon).</summary>
    public bool Contains(float x, float z) =>
        ReachHorizon || (MathF.Abs(x - Position.X) <= _width * 0.5f && MathF.Abs(z - Position.Z) <= _depth * 0.5f);

    // Things that move through the water leave a wake: their last place and when they last made a ring.
    private readonly Dictionary<GameObject, (Vector3 position, bool inside, float lastRing)> _movers = new();
    private readonly List<GameObject> _gone = new();

    /// <summary>
    /// Splashes and wakes from the scene's moving objects (those driven by a script or OnUpdate): a splash when one
    /// falls in, rings behind it while it moves through the water. Called by <see cref="Scene.Update"/>.
    /// </summary>
    internal void TrackMovers(IEnumerable<GameObject> movers, float deltaTime)
    {
        if (Interaction <= 0f || deltaTime <= 0f) return;
        foreach (var obj in movers)
        {
            var bounds = obj.Bounds;
            var center = bounds.Center;
            bool touching = Contains(center.X, center.Z) && bounds.Min.Y < Level + 0.05f && bounds.Max.Y > Level - 0.05f;
            if (!_movers.TryGetValue(obj, out var state))
            {
                _movers[obj] = (center, touching, Time);
                continue;
            }

            var velocity = (center - state.position) / deltaTime;
            float speed = velocity.Length;
            float size = MathF.Max(bounds.Size.X, bounds.Size.Z);
            if (touching && !state.inside && velocity.Y < -0.5f)
            {
                Splash(new Vector3(center.X, Level, center.Z), MathF.Min(-velocity.Y * 0.35f * (0.5f + size), 2.5f) * Interaction);
                state.lastRing = Time;
            }
            else if (touching && new Vector2(velocity.X, velocity.Z).Length > 0.3f && Time - state.lastRing > 0.3f)
            {
                Splash(new Vector3(center.X, Level, center.Z), MathF.Min(speed * 0.12f * (0.5f + size), 0.8f) * Interaction);
                state.lastRing = Time;
            }
            _movers[obj] = (center, touching, state.lastRing);
        }

        foreach (var obj in _movers.Keys) if (obj.IsDestroyed) _gone.Add(obj);
        foreach (var obj in _gone) _movers.Remove(obj);
        _gone.Clear();
    }

    /// <summary>Brings the FFT waves to the current time and uploads them (GL thread; the renderer calls it).</summary>
    internal void UpdateOcean()
    {
        Ocean ??= new OceanWaves();
        // The patch holds the shorter waves; its larger, turned copy (CascadeScale) carries waves of WaveLength.
        Ocean.Update(Time, WaveLength * CascadeScale, WaveHeight, Choppiness, WindDirection, DirectionSpread);
    }

    /// <summary>
    /// Approximate surface height at a world position, for floating objects (vertical wave motion only, so it ignores
    /// the sideways shift of choppy crests). Before the water is first drawn it is the flat level.
    /// </summary>
    public float GetHeightAt(float x, float z)
    {
        if (Ocean == null) return Position.Y;
        var large = CascadePoint(new Vector2(x, z), Ocean.PatchSize);
        return Position.Y + Ocean.GetHeight(large.X, large.Y) + CascadeDetailWeight * Ocean.GetHeight(x, z);
    }

    /// <summary>Where the larger, turned copy of the patch is sampled for world point <paramref name="p"/> (same as the shaders).</summary>
    private static Vector2 CascadePoint(Vector2 p, float patch)
    {
        float c = MathF.Cos(CascadeAngle), s = MathF.Sin(CascadeAngle);
        var q = p * CascadeScale;
        return new Vector2(c * q.X - s * q.Y, s * q.X + c * q.Y) + new Vector2(patch * 0.5f);
    }

    // ---- quadtree of tiles -----------------------------------------------------------------------

    /// <summary>One tile to draw: its corner (x, z), side length and quadtree level (0 = smallest).</summary>
    internal readonly record struct WaterNode(Vector2 Origin, float Size, int Level, int RootLevel);

    internal IReadOnlyList<WaterNode> Nodes => _nodes;
    internal Mesh Grid => _grid!;
    internal int GridCells => _gridCells;

    /// <summary>The rectangle covered this frame, (min x, min z, max x, max z).</summary>
    internal Vector4 Coverage => new(_coverMin.X, _coverMin.Y, _coverMax.X, _coverMax.Y);

    private float MinTile => MathF.Max(TileSize, 1f);
    private float Ranges => MathF.Max(LodDistance, MinTile * 2f);

    /// <summary>Distance within which tiles of <paramref name="level"/> (and smaller) are used.</summary>
    internal float Range(int level) => Ranges * MathF.Pow(2f, level);

    /// <summary>
    /// Picks the tiles to draw for a camera at <paramref name="eye"/>: starting from tiles that cover the whole surface,
    /// each tile closer than its range is split into four, down to <see cref="TileSize"/>. Tiles outside
    /// <paramref name="frustum"/> are skipped. A sea is laid out around the camera, out to <paramref name="far"/>.
    /// </summary>
    internal void SelectNodes(Vector3 eye, float far, Frustum? frustum)
    {
        int cells = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Clamp(Detail, 4, 256));
        if (_grid == null || cells != _gridCells)
        {
            _grid?.Dispose();
            _grid = BuildGrid(cells);
            _gridCells = cells;
        }

        float extent;
        if (ReachHorizon)
        {
            extent = MathF.Max(far * 0.98f, MinTile);
            _coverMin = eye.Xz - new Vector2(extent);
            _coverMax = eye.Xz + new Vector2(extent);
            extent *= 2f;
        }
        else
        {
            _coverMin = Position.Xz - Size * 0.5f;
            _coverMax = Position.Xz + Size * 0.5f;
            extent = MathF.Max(_width, _depth);
        }

        int rootLevel = Math.Clamp((int)MathF.Ceiling(MathF.Log2(MathF.Max(extent / MinTile, 1f))), 0, 14);
        // Never larger than needed for the view distance: past the farthest range, tiles stop growing.
        while (rootLevel > 0 && Range(rootLevel - 1) > extent * 2f) rootLevel--;
        float rootSize = MinTile * MathF.Pow(2f, rootLevel);

        // A sea's tiles sit on a fixed world grid, so its vertices do not slide while the camera moves.
        var start = ReachHorizon
            ? new Vector2(MathF.Floor(_coverMin.X / rootSize) * rootSize, MathF.Floor(_coverMin.Y / rootSize) * rootSize)
            : _coverMin;

        _nodes.Clear();
        for (float z = start.Y; z < _coverMax.Y; z += rootSize)
        for (float x = start.X; x < _coverMax.X; x += rootSize)
            Visit(new Vector2(x, z), rootSize, rootLevel, rootLevel, eye, frustum);
    }

    private void Visit(Vector2 origin, float size, int level, int rootLevel, Vector3 eye, Frustum? frustum)
    {
        var min = new Vector3(origin.X, Position.Y - WaveHeight * 3f - SkirtDepth, origin.Y);
        var max = new Vector3(origin.X + size, Position.Y + WaveHeight * 3f, origin.Y + size);
        if (max.X <= _coverMin.X || max.Z <= _coverMin.Y || min.X >= _coverMax.X || min.Z >= _coverMax.Y) return;
        var box = new Aabb(min, max);
        if (frustum is { } f && !f.Intersects(box)) return;

        float distance = (eye - Vector3.Clamp(eye, min, max)).Length;
        if (level > 0 && distance <= Range(level - 1))
        {
            float half = size * 0.5f;
            Visit(origin, half, level - 1, rootLevel, eye, frustum);
            Visit(origin + new Vector2(half, 0f), half, level - 1, rootLevel, eye, frustum);
            Visit(origin + new Vector2(0f, half), half, level - 1, rootLevel, eye, frustum);
            Visit(origin + new Vector2(half, half), half, level - 1, rootLevel, eye, frustum);
            return;
        }
        _nodes.Add(new WaterNode(origin, size, level, rootLevel));
    }

    /// <summary>The model matrix of a tile: the unit grid scaled to its size, with the skirts stretched to <see cref="SkirtDepth"/>.</summary>
    internal Matrix4 NodeMatrix(WaterNode node) =>
        Matrix4.CreateScale(node.Size, SkirtDepth, node.Size) * Matrix4.CreateTranslation(node.Origin.X, Position.Y, node.Origin.Y);

    /// <summary>
    /// Distances (start, end) over which a tile morphs into the grid of the next larger tile: it ends exactly where
    /// that larger tile takes over, so neighbours of different sizes meet without a seam or a pop.
    /// </summary>
    internal Vector2 MorphRange(WaterNode node)
    {
        if (node.Level >= node.RootLevel) return new Vector2(1e9f, 2e9f);   // the largest tiles never morph
        float end = Range(node.Level);
        return new Vector2(end * 0.7f, end);
    }

    /// <summary>How far the skirts under the tile edges hang (they hide any crack between tiles of different sizes).</summary>
    private float SkirtDepth => MathF.Max(0.3f, WaveHeight * 3f);

    /// <summary>
    /// A flat grid over the unit square with <paramref name="cells"/> cells per side, facing up, plus a strip hanging
    /// from each edge down to y = -1 (both sides, so it shows from anywhere) that fills any gap next to a finer tile.
    /// </summary>
    private static Mesh BuildGrid(int cells)
    {
        int stride = cells + 1;
        var vertices = new List<Vertex>(stride * stride + stride * 4);
        for (int z = 0; z <= cells; z++)
        for (int x = 0; x <= cells; x++)
            vertices.Add(new Vertex(new Vector3((float)x / cells, 0f, (float)z / cells), Vector3.UnitY, Vector2.Zero));

        var indices = new List<uint>(cells * cells * 6 + cells * 4 * 12);
        for (int z = 0; z < cells; z++)
        for (int x = 0; x < cells; x++)
        {
            uint a = (uint)(z * stride + x), b = a + (uint)stride;
            indices.AddRange(new[] { a, b, b + 1, a, b + 1, a + 1 });
        }

        var edges = new[]
        {
            Enumerable.Range(0, stride).Select(x => x),                          // z = 0
            Enumerable.Range(0, stride).Select(x => cells * stride + x),         // z = 1
            Enumerable.Range(0, stride).Select(z => z * stride),                 // x = 0
            Enumerable.Range(0, stride).Select(z => z * stride + cells),         // x = 1
        };
        foreach (var edge in edges)
        {
            var top = edge.ToArray();
            uint first = (uint)vertices.Count;
            foreach (int t in top) vertices.Add(new Vertex(vertices[t].Position - Vector3.UnitY, Vector3.UnitY, Vector2.Zero));
            for (int k = 0; k < top.Length - 1; k++)
            {
                uint t0 = (uint)top[k], t1 = (uint)top[k + 1], b0 = first + (uint)k, b1 = b0 + 1;
                indices.AddRange(new[] { t0, b0, t1, t1, b0, b1 });   // one side
                indices.AddRange(new[] { t0, t1, b0, t1, b1, b0 });   // and the other
            }
        }
        return new Mesh(vertices.ToArray(), indices.ToArray());
    }

    /// <summary>The reflection render target, (re)created at the requested size.</summary>
    internal Framebuffer GetReflectionBuffer(int width, int height)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);
        if (ReflectionBuffer == null)
            ReflectionBuffer = Framebuffer.CreateColor(width, height, 0, hdr: true, DepthMode.Renderbuffer);
        else
            ReflectionBuffer.Resize(width, height);
        return ReflectionBuffer;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _grid?.Dispose();
        Ocean?.Dispose();
        ReflectionBuffer?.Dispose();
    }
}
