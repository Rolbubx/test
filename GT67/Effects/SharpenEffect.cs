using GT67.Core;

namespace GT67.Effects;

/// <summary>Makes the image crisper: sharpens fine detail and adds a little local contrast ("clarity").</summary>
public sealed class SharpenEffect : SinglePassEffect
{
    protected override string ShaderName => "fx_sharpen";
    public override PostStage Stage => PostStage.Ldr;

    /// <summary>How strongly fine detail is sharpened (0 = off).</summary>
    public float Strength { get; set; } = 0.6f;

    /// <summary>Local contrast on larger shapes (0 = off).</summary>
    public float Clarity { get; set; } = 0.25f;

    protected override void SetUniforms(Shader shader, PostContext context)
    {
        shader.SetFloat("uStrength", Strength);
        shader.SetFloat("uClarity", Clarity);
    }
}
