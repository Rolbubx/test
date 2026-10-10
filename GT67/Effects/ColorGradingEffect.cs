using GT67.Core;
using OpenTK.Mathematics;

namespace GT67.Effects;

/// <summary>Simple colour grading on the final image: brightness, contrast, saturation and tint.</summary>
public sealed class ColorGradingEffect : SinglePassEffect
{
    protected override string ShaderName => "fx_grade";
    public override PostStage Stage => PostStage.Ldr;

    public float Brightness { get; set; }
    public float Contrast { get; set; } = 1.08f;
    public float Saturation { get; set; } = 1.12f;

    /// <summary>Multiplied with the image, e.g. warm = (1.05, 1.0, 0.92).</summary>
    public Color4 Tint { get; set; } = Color4.White;

    protected override void SetUniforms(Shader shader, PostContext context)
    {
        shader.SetFloat("uBrightness", Brightness);
        shader.SetFloat("uContrast", Contrast);
        shader.SetFloat("uSaturation", Saturation);
        shader.SetVector3("uTint", new Vector3(Tint.R, Tint.G, Tint.B));
    }
}
