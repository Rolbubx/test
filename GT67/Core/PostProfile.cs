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
