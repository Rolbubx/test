using GT67.Core;

namespace GT67.Effects;

/// <summary>Lens-style colour fringing that grows towards the screen edges.</summary>
public sealed class ChromaticAberrationEffect : SinglePassEffect
{
    protected override string ShaderName => "fx_chroma";

    /// <summary>Fringe width as a fraction of the distance from the centre (0.005 is subtle).</summary>
    public float Strength { get; set; } = 0.006f;

    protected override void SetUniforms(Shader shader, PostContext context) =>
        shader.SetFloat("uStrength", Strength);
}
