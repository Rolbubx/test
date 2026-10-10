using GT67.Utils;

namespace GT67.Core;

/// <summary>
/// One or more mesh/material parts. Reference counted: every GameObject using the model holds a reference,
/// and the model frees its GPU data when the last one is released. Cached models are kept alive by the cache.
/// </summary>
public sealed class Model : IDisposable
{
    private readonly List<ModelPart> _parts;
    private readonly List<Texture> _ownedTextures;
    private int _refCount;
    private TriangleBvh? _meshBvh, _boxBvh;
    private bool _disposed;

    public string Name { get; }
    public IReadOnlyList<ModelPart> Parts => _parts;
    public Aabb Bounds { get; private set; }

    /// <summary>Absolute path of the file this model was imported from (null for procedural models).</summary>
    public string? SourcePath { get; internal set; }

    /// <summary>Every file the import needed: the model itself, its textures, .mtl/.bin companions.</summary>
    public IReadOnlyList<string> SourceFiles { get; internal set; } = Array.Empty<string>();

    public Model(string name, IEnumerable<ModelPart> parts, IEnumerable<Texture>? ownedTextures = null)
    {
        Name = name;
        _parts = parts.ToList();
        _ownedTextures = ownedTextures?.ToList() ?? new List<Texture>();

        RecalculateBounds();
    }

    internal TriangleBvh GetMeshBvh()
    {
        if (_meshBvh == null && _meshBvhTask != null)
        {
            _meshBvh = _meshBvhTask.Result;   // waits only if the background build has not finished yet
            _meshBvhTask = null;
        }
        return _meshBvh ??= TriangleBvh.FromModel(this);
    }

    private Task<TriangleBvh>? _meshBvhTask;

    /// <summary>
    /// Starts building the collision data on a background thread, so it is ready (or nearly) by the first collision
    /// query instead of stalling a frame. Loading a model does this by itself.
    /// </summary>
    public void PrepareCollision()
    {
        if (_meshBvh != null || _meshBvhTask != null) return;
        _meshBvhTask = Task.Run(() => TriangleBvh.FromModel(this));
    }

    internal TriangleBvh GetBoxBvh() => _boxBvh ??= TriangleBvh.FromBox(Bounds);

    /// <summary>Forget the cached collision data after the mesh changed (it is rebuilt on the next query).</summary>
    public void InvalidateCollision()
    {
        _meshBvhTask = null;   // a build still running uses the old mesh: drop it
        _meshBvh = null;
        _boxBvh = null;
    }

    /// <summary>Recomputes <see cref="Bounds"/> after a mesh was edited (terrain sculpting).</summary>
    public void RecalculateBounds()
    {
        var bounds = Aabb.Empty;
        foreach (var part in _parts) bounds = bounds.Encapsulate(part.Mesh.Bounds);
        Bounds = bounds.IsValid ? bounds : new Aabb(OpenTK.Mathematics.Vector3.Zero, OpenTK.Mathematics.Vector3.Zero);
    }

    /// <summary>Wraps a single mesh. The model takes ownership of the mesh.</summary>
    public static Model FromMesh(Mesh mesh, Material? material = null, string name = "mesh") =>
        new(name, new[] { new ModelPart(mesh, material ?? new Material(), name) });

    /// <summary>Loads (or returns the cached) model for <paramref name="path"/>.</summary>
    public static Model Load(string path) => ModelCache.GetModel(path);

    /// <summary>Like <see cref="Load"/> but logs a warning and returns null instead of throwing.</summary>
    public static Model? TryLoad(string path)
    {
        try
        {
            return Load(path);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            Log.Warn($"Could not load model '{path}': {e.Message}");
            return null;
        }
    }

    internal void Retain() => _refCount++;

    internal void Release()
    {
        if (_disposed) return;
        if (--_refCount <= 0) Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var part in _parts) part.Mesh.Dispose();
        foreach (var texture in _ownedTextures) texture.Dispose();
    }
}
