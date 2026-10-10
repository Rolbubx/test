using System.Diagnostics;
using OpenTK.Graphics.OpenGL4;

namespace GT67.Core;

public enum ToneMapOperator
{
    Reinhard = 0,
    Aces = 1,
}

/// <summary>
/// Screen-space pipeline: HDR effects, then exposure + tone mapping + gamma, then LDR effects.
/// Add effects in code: <c>renderer.Post.Add(new BloomEffect())</c>.
/// </summary>
public sealed class PostProcess : IDisposable
{
    private readonly Mesh _quad = Utils.Primitives.Quad();
    private readonly PostContext _context;
    private readonly List<PostEffect> _effects = new();
    private readonly List<PostEffect> _hdrRun = new();
    private readonly List<PostEffect> _ldrRun = new();
    private readonly Framebuffer?[] _hdrTargets = new Framebuffer?[2];
    private readonly Framebuffer?[] _ldrTargets = new Framebuffer?[2];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private float _lastTime;

    /// <summary>When off, the HDR colour is only clamped (and stays linear, so it looks dark).</summary>
    public bool Enabled { get; set; } = true;
    public float Exposure { get; set; } = 1f;
    public float Gamma { get; set; } = 2.2f;
    public ToneMapOperator ToneMap { get; set; } = ToneMapOperator.Aces;

    /// <summary>Set by the renderer each frame: the sun shadow map for effects that march through the scene.</summary>
    internal int Quality { get; set; }
    internal int ShadowTexture { get; set; }
    internal Matrix4 ShadowMatrix { get; set; }

    public IReadOnlyList<PostEffect> Effects => _effects;

    public PostProcess(string shaderDirectory) => _context = new PostContext(shaderDirectory, _quad);

    // ---- effect management -----------------------------------------------------------------------

    public T Add<T>(T effect) where T : PostEffect
    {
        _effects.Add(effect);
        return effect;
    }

    /// <summary>Removes and disposes the effect.</summary>
    public void Remove(PostEffect effect)
    {
        if (_effects.Remove(effect)) effect.Dispose();
    }

    public T? Get<T>() where T : PostEffect => _effects.OfType<T>().FirstOrDefault();

    public IEnumerable<Shader> EffectShaders => _effects.SelectMany(e => e.Shaders);

    // ---- by-name access (for scripts) ------------------------------------------------------------

    /// <summary>
    /// Finds the first effect called <paramref name="name"/> ("Bloom", "DepthOfField", "SunRays"… or the class name
    /// such as "BloomEffect"; case-insensitive). "ToneMapping" returns this pipeline's own exposure/gamma settings.
    /// </summary>
    public object? Find(string name)
    {
        if (name.Equals("ToneMapping", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("ToneMap", StringComparison.OrdinalIgnoreCase)) return this;

        return _effects.FirstOrDefault(e =>
            e.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            e.GetType().Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Sets one parameter of an effect by name, e.g. <c>post.Set("Bloom", "Intensity", 0.8f)</c>,
    /// <c>post.Set("ColorCorrection", "Lift", (0.02f, 0f, 0.05f))</c> or <c>post.Set("ToneMapping", "ToneMap", "Reinhard")</c>.
    /// Returns false (and logs why) if the effect or parameter does not exist or the value does not fit.
    /// </summary>
    public bool Set(string effect, string parameter, object? value)
    {
        var target = Find(effect);
        if (target == null)
        {
            Utils.Log.Warn($"Post effect '{effect}' not found (known: {string.Join(", ", KnownNames())}).");
            return false;
        }

        var descriptor = ParameterAccess.Find(target, parameter);
        if (descriptor == null)
        {
            Utils.Log.Warn($"'{effect}' has no parameter '{parameter}' (known: {string.Join(", ", ParameterAccess.Describe(target).Select(p => p.Name))}).");
            return false;
        }

        if (!descriptor.TrySet(value))
        {
            Utils.Log.Warn($"Cannot set {effect}.{descriptor.Name} ({descriptor.Type.Name}) from '{value}'.");
            return false;
        }
        return true;
    }

    /// <summary>Reads one parameter of an effect by name; null if the effect or parameter does not exist.</summary>
    public object? Get(string effect, string parameter) =>
        Find(effect) is { } target ? ParameterAccess.Get(target, parameter) : null;

    /// <summary>One line per parameter of every effect, e.g. <c>Bloom.Intensity: Single = 0.5</c>.</summary>
    public IEnumerable<string> Describe()
    {
        foreach (var p in ParameterAccess.Describe(this)) yield return $"ToneMapping.{p}";
        foreach (var effect in _effects)
            foreach (var p in effect.Parameters)
                yield return $"{effect.Name}.{p}";
    }

    private IEnumerable<string> KnownNames() => _effects.Select(e => e.Name).Prepend("ToneMapping");

    // ---- frame -----------------------------------------------------------------------------------

    /// <summary>Runs the whole chain and leaves the result in the window's framebuffer.</summary>
    public void Render(Shader toneMapShader, int sceneTexture, int depthTexture, Camera camera, Scene scene,
        int width, int height, Vector2i outputOffset = default)
    {
        _context.OutputOffset = outputOffset;
        float now = (float)_clock.Elapsed.TotalSeconds;
        _context.Width = width;
        _context.Height = height;
        _context.DepthTexture = depthTexture;
        _context.Camera = camera;
        _context.Scene = scene;
        _context.Quality = Quality;
        _context.ShadowTexture = ShadowTexture;
        _context.ShadowMatrix = ShadowMatrix;
        _context.DeltaTime = now - _lastTime;
        _context.Time = now;
        _lastTime = now;

        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.Blend);
        GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);

        _hdrRun.Clear();
        _ldrRun.Clear();
        foreach (var effect in _effects)
        {
            if (!effect.Enabled || !effect.ShouldRun(_context)) continue;
            (effect.Stage == PostStage.Hdr ? _hdrRun : _ldrRun).Add(effect);
        }

        int texture = sceneTexture;
        int pingPong = 0;

        foreach (var effect in _hdrRun)
        {
            var target = GetTarget(_hdrTargets, pingPong, width, height, hdr: true);
            texture = RunEffect(effect, target, texture);
            pingPong ^= 1;
        }

        if (_ldrRun.Count == 0)
        {
            _context.Output = null;
            Framebuffer.BindDefault(_context.OutputOffset.X, _context.OutputOffset.Y, width, height);
            DrawToneMap(toneMapShader, texture);
            return;
        }

        var ldr = GetTarget(_ldrTargets, 0, width, height, hdr: false);
        _context.Output = ldr;
        ldr.Bind();
        DrawToneMap(toneMapShader, texture);
        texture = ldr.ColorTexture;
        pingPong = 1;

        for (int i = 0; i < _ldrRun.Count; i++)
        {
            bool last = i == _ldrRun.Count - 1;
            var target = last ? null : GetTarget(_ldrTargets, pingPong, width, height, hdr: false);
            texture = RunEffect(_ldrRun[i], target, texture);
            pingPong ^= 1;
        }
        _context.Output = null;
    }

    private int RunEffect(PostEffect effect, Framebuffer? target, int input)
    {
        _context.Output = target;
        _context.BindOutput();
        GL.Disable(EnableCap.Blend);
        effect.Apply(_context, input);
        return target?.ColorTexture ?? 0;
    }

    private static Framebuffer GetTarget(Framebuffer?[] targets, int index, int width, int height, bool hdr)
    {
        var target = targets[index];
        if (target == null) return targets[index] = Framebuffer.CreateColor(width, height, 0, hdr, DepthMode.None);
        target.Resize(width, height);
        return target;
    }

    private void DrawToneMap(Shader shader, int sceneTexture)
    {
        shader.Use();
        shader.SetInt("uScene", 0);
        shader.SetBool("uEnabled", Enabled);
        shader.SetFloat("uExposure", Exposure);
        shader.SetFloat("uGamma", Gamma);
        shader.SetInt("uToneMap", (int)ToneMap);

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, sceneTexture);
        _quad.Draw();
    }

    public void Dispose()
    {
        foreach (var effect in _effects) effect.Dispose();
        _effects.Clear();
        foreach (var target in _hdrTargets) target?.Dispose();
        foreach (var target in _ldrTargets) target?.Dispose();
        _quad.Dispose();
    }
}
