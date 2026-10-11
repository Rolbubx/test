using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>
/// The post-processing settings of one scene (its "Volume", as in Unity): tone mapping and the settings of every
/// effect, by name. The effects themselves live in <see cref="Renderer.Post"/>; when a scene becomes active its profile
/// is applied to them, on top of the start-up values from Program.cs. Set values in code like the pipeline itself:
/// <c>scene.Volume.Set("Bloom", "Intensity", 0.8f)</c>, <c>scene.Volume.Set("ToneMapping", "Exposure", 1.2f)</c>.
/// </summary>
public sealed class PostProfile
{
    private readonly List<(string effect, string parameter, object? value)> _values = new();
    private readonly List<ScreenShaderUse> _screenShaders = new();

    /// <summary>
    /// The scene's screen shaders (.shader files with a <c>screen</c> block: PS1, CRT, your own), run in this order after
    /// the built-in effects of the same stage. They are read every frame, so changing them takes effect at once.
    /// </summary>
    public IReadOnlyList<ScreenShaderUse> ScreenShaders => _screenShaders;

    /// <summary>Adds a screen shader by name (its .shader file in Shaders/Custom), e.g. <c>scene.Volume.AddScreenShader("PS1")</c>.</summary>
    public ScreenShaderUse AddScreenShader(string name)
    {
        var use = new ScreenShaderUse(name);
        _screenShaders.Add(use);
        return use;
    }

    /// <summary>The first use of a screen shader by name, or null.</summary>
    public ScreenShaderUse? GetScreenShader(string name) =>
        _screenShaders.FirstOrDefault(s => Same(s.Name, name));

    public void RemoveScreenShader(ScreenShaderUse use) => _screenShaders.Remove(use);

    /// <summary>Moves a screen shader earlier (-1) or later (+1) in the chain.</summary>
    public void MoveScreenShader(ScreenShaderUse use, int direction)
    {
        int index = _screenShaders.IndexOf(use), target = index + direction;
        if (index < 0 || target < 0 || target >= _screenShaders.Count) return;
        (_screenShaders[index], _screenShaders[target]) = (_screenShaders[target], _screenShaders[index]);
    }

    /// <summary>True when the scene keeps the start-up settings.</summary>
    public bool IsEmpty => _values.Count == 0;

    /// <summary>Every setting, in the order it was set.</summary>
    public IReadOnlyList<(string effect, string parameter, object? value)> Values => _values;

    /// <summary>Sets one setting of an effect ("Bloom", "DepthOfField"…, or "ToneMapping" for exposure and tone map).</summary>
    public void Set(string effect, string parameter, object? value)
    {
        int index = _values.FindIndex(v => Same(v.effect, effect) && Same(v.parameter, parameter));
        if (index >= 0) _values[index] = (effect, parameter, value);
        else _values.Add((effect, parameter, value));
    }

    /// <summary>The stored value of a setting, or null if the profile does not change it.</summary>
    public object? Get(string effect, string parameter) =>
        _values.FirstOrDefault(v => Same(v.effect, effect) && Same(v.parameter, parameter)).value;

    public void Clear() => _values.Clear();

    /// <summary>Every setting the pipeline has right now: tone mapping and each effect, its on/off switch included.</summary>
    public static PostProfile Capture(PostProcess post)
    {
        var profile = new PostProfile();
        profile.CaptureFrom(post);
        return profile;
    }

    /// <summary>Replaces this profile's settings with what the pipeline has right now.</summary>
    public void CaptureFrom(PostProcess post)
    {
        _values.Clear();
        foreach (var p in ParameterAccess.Describe(post)) _values.Add(("ToneMapping", p.Name, p.Value));
        foreach (var effect in post.Effects)
            foreach (var p in ParameterAccess.Describe(effect))
                _values.Add((effect.Name, p.Name, p.Value));
    }

    /// <summary>Writes these settings into the pipeline (unknown effects or settings are skipped with a warning).</summary>
    public void ApplyTo(PostProcess post)
    {
        foreach (var (effect, parameter, value) in _values) post.Set(effect, parameter, value);
    }

    private static bool Same(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// One screen shader in a scene's Volume, with its settings. Settings not set keep the defaults of the .shader file's
/// <c>param</c> lines. From a script: <c>Game.Scene.Volume.GetScreenShader("PS1")?.Set("Pixelation", 4f)</c>.
/// </summary>
public sealed class ScreenShaderUse
{
    private readonly Dictionary<string, Vector4> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _textures = new(StringComparer.OrdinalIgnoreCase);

    public ScreenShaderUse(string name) => Name = name;

    /// <summary>The shader's name (its file name without .shader).</summary>
    public string Name { get; }

    public bool Enabled { get; set; } = true;

    /// <summary>The settings that differ from the file's defaults (float in X; colours and vectors in XYZ; bools 0/1).</summary>
    public IReadOnlyDictionary<string, Vector4> Values => _values;

    /// <summary>Images given to <c>texture</c> settings (paths relative to the executable).</summary>
    public IReadOnlyDictionary<string, string> Textures => _textures;

    public void Set(string parameter, float value) => _values[parameter] = new Vector4(value, 0f, 0f, 0f);
    public void Set(string parameter, bool value) => _values[parameter] = new Vector4(value ? 1f : 0f, 0f, 0f, 0f);
    public void Set(string parameter, Vector2 value) => _values[parameter] = new Vector4(value.X, value.Y, 0f, 0f);
    public void Set(string parameter, Vector3 value) => _values[parameter] = new Vector4(value, 0f);
    public void Set(string parameter, Color4 value) => _values[parameter] = new Vector4(value.R, value.G, value.B, 0f);
    public void Set(string parameter, Vector4 value) => _values[parameter] = value;
    public void SetTexture(string parameter, string path) => _textures[parameter] = path;

    /// <summary>Forgets a setting, so it is back to the file's default.</summary>
    public void Reset(string parameter)
    {
        _values.Remove(parameter);
        _textures.Remove(parameter);
    }

    /// <summary>The value of a setting: what was set, else the default from the shader's param line.</summary>
    public Vector4 Get(ShaderParam param) => _values.TryGetValue(param.Name, out var value) ? value : param.Default;

    /// <summary>The image of a texture setting: what was set, else the default from the shader's param line.</summary>
    public string GetTexture(ShaderParam param) => _textures.TryGetValue(param.Name, out var path) ? path : param.DefaultTexture;
}
