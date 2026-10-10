using GT67.Core;
using GT67.Utils;
using OpenTK.Mathematics;

namespace GT67.Effects;

/// <summary>
/// Full colour correction on the final image. Every default is neutral, so adding it changes nothing until you
/// set something: exposure, white balance (temperature / tint), lift-gamma-gain, contrast, hue, saturation, vibrance.
/// </summary>
public sealed class ColorCorrectionEffect : SinglePassEffect
{
    protected override string ShaderName => "fx_correct";
    public override PostStage Stage => PostStage.Ldr;

    /// <summary>Exposure offset in stops (+1 doubles the brightness).</summary>
    public float Exposure { get; set; }

    /// <summary>White balance, -1 (cool/blue) to +1 (warm/orange).</summary>
    public float Temperature { get; set; }

    /// <summary>White balance, -1 (green) to +1 (magenta).</summary>
    public float Tint { get; set; }

    /// <summary>Hue rotation in degrees.</summary>
    public float HueShift { get; set; }

    public float Saturation { get; set; } = 1f;

    /// <summary>Saturation boost that favours dull colours (0 = off).</summary>
    public float Vibrance { get; set; }

    public float Contrast { get; set; } = 1f;
    public float Brightness { get; set; }

    /// <summary>Added to the shadows, per channel (e.g. (0.02, 0, 0.04) for cool shadows).</summary>
    public Vector3 Lift { get; set; } = Vector3.Zero;

    /// <summary>Mid-tone curve per channel; values above 1 brighten the mid-tones.</summary>
    public Vector3 Gamma { get; set; } = Vector3.One;

    /// <summary>Multiplier for the highlights, per channel (e.g. (1.1, 1.0, 0.9) for warm highlights).</summary>
    public Vector3 Gain { get; set; } = Vector3.One;

    protected override void SetUniforms(Shader shader, PostContext context)
    {
        shader.SetFloat("uExposure", Exposure);
        shader.SetFloat("uTemperature", Temperature);
        shader.SetFloat("uTint", Tint);
        shader.SetFloat("uHue", HueShift * MathUtil.Deg2Rad);
        shader.SetFloat("uSaturation", Saturation);
        shader.SetFloat("uVibrance", Vibrance);
        shader.SetFloat("uContrast", Contrast);
        shader.SetFloat("uBrightness", Brightness);
        shader.SetVector3("uLift", Lift);
        shader.SetVector3("uGamma", Gamma);
        shader.SetVector3("uGain", Gain);
    }
}
