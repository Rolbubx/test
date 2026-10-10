using GT67.Core;

namespace GT67.Effects;

/// <summary>Darkens the corners of the screen.</summary>
public sealed class VignetteEffect : SinglePassEffect
{
    protected override string ShaderName => "fx_vignette";
    public override PostStage Stage => PostStage.Ldr;

    public float Intensity { get; set; } = 0.5f;

    /// <summary>Distance from the centre where the darkening ends (roughly 0..1).</summary>
    public float Radius { get; set; } = 1.0f;

    public float Softness { get; set; } = 0.6f;

    /// <summary>0 follows the screen shape, 1 is a circle.</summary>
    public float Roundness { get; set; } = 0.5f;

    protected override void SetUniforms(Shader shader, PostContext context)
    {
        shader.SetFloat("uIntensity", Intensity);
        shader.SetFloat("uRadius", Radius);
        shader.SetFloat("uSoftness", Softness);
        shader.SetFloat("uRoundness", Roundness);
    }
}
