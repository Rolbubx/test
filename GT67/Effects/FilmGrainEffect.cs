using GT67.Core;

namespace GT67.Effects;

/// <summary>Animated film grain, strongest in the mid-tones.</summary>
public sealed class FilmGrainEffect : SinglePassEffect
{
    protected override string ShaderName => "fx_grain";
    public override PostStage Stage => PostStage.Ldr;

    public float Strength { get; set; } = 0.05f;

    protected override void SetUniforms(Shader shader, PostContext context)
    {
        shader.SetFloat("uStrength", Strength);
        shader.SetFloat("uTime", context.Time);
    }
}
