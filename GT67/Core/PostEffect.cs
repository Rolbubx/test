namespace GT67.Core;

public enum PostStage
{
    /// <summary>Runs on linear HDR colour, before tone mapping (bloom, depth of field, sun rays…).</summary>
    Hdr,

    /// <summary>Runs on the final display-ready colour, after tone mapping and gamma (vignette, grain…).</summary>
    Ldr,
}

/// <summary>
/// A screen-space effect. Add instances in code with <c>renderer.Post.Add(new BloomEffect())</c>;
/// effects run in the order they were added, HDR ones first, then tone mapping, then LDR ones.
/// </summary>
public abstract class PostEffect : IDisposable
{
    private readonly List<Shader> _shaders = new();

    public virtual string Name => GetType().Name.Replace("Effect", "");
    public bool Enabled { get; set; } = true;
    public virtual PostStage Stage => PostStage.Hdr;

    /// <summary>Every setting of this effect as a name/type/value (see <see cref="ParameterAccess"/>).</summary>
    public IReadOnlyList<ParameterDescriptor> Parameters => ParameterAccess.Describe(this);

    /// <summary>Sets a setting by (case-insensitive) name, e.g. <c>bloom.SetParameter("Intensity", 0.8f)</c>.</summary>
    public bool SetParameter(string name, object? value) => ParameterAccess.TrySet(this, name, value);

    /// <summary>Reads a setting by name; null if there is no such parameter.</summary>
    public object? GetParameter(string name) => ParameterAccess.Get(this, name);

    /// <summary>Shaders owned by this effect (included in F5 hot reload).</summary>
    public IReadOnlyList<Shader> Shaders => _shaders;

    /// <summary>Return false to skip the effect this frame (e.g. the sun is behind the camera).</summary>
    public virtual bool ShouldRun(PostContext context) => true;

    /// <summary>
    /// Reads <paramref name="input"/> and draws the result into the target the chain bound
    /// (call <see cref="PostContext.BindOutput"/> to return to it after using internal targets).
    /// </summary>
    public abstract void Apply(PostContext context, int input);

    protected Shader LoadShader(PostContext context, string fragmentName)
    {
        var shader = context.LoadShader(fragmentName);
        _shaders.Add(shader);
        return shader;
    }

    public virtual void Dispose()
    {
        foreach (var shader in _shaders) shader.Dispose();
        _shaders.Clear();
    }
}
