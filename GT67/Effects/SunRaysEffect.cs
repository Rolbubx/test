using GT67.Core;
using GT67.Utils;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GT67.Effects;

/// <summary>
/// Screen-space light shafts ("god rays") from the scene's directional light: the sky around the sun is
/// radially blurred towards it, and objects in front of the sky block the rays.
/// </summary>
public sealed class SunRaysEffect : PostEffect
{
    private Shader? _mask, _blur, _add;
    private Framebuffer? _maskTarget, _rayTarget;
    private Vector2 _sunUv;
    private float _fade;

    public float Intensity { get; set; } = 0.6f;

    /// <summary>How far the rays reach towards the sun (1 = all the way).</summary>
    public float Density { get; set; } = 0.95f;

    /// <summary>Per-step falloff along each ray (closer to 1 = longer rays).</summary>
    public float Decay { get; set; } = 0.965f;

    public float Weight { get; set; } = 0.04f;

    /// <summary>Radius of the bright sun disc, in screen-height units.</summary>
    public float SunSize { get; set; } = 0.035f;

    public float DiscBrightness { get; set; } = 6f;

    public override bool ShouldRun(PostContext context)
    {
        var sun = context.Scene.Sun;
        if (sun is not { IsEmitting: true }) return false;

        // The sun is infinitely far away in the direction opposite to the light's travel direction.
        var camera = context.Camera;
        var sunWorld = camera.Position - sun.Direction * 1000f;
        var clip = Vector4.TransformRow(new Vector4(sunWorld, 1f), camera.ViewMatrix * camera.ProjectionMatrix);
        if (clip.W <= 0.001f) return false; // behind the camera

        var ndc = clip.Xy / clip.W;
        _sunUv = ndc * 0.5f + new Vector2(0.5f);

        // Fade out as the sun leaves the screen.
        float offscreen = MathF.Max(MathF.Abs(ndc.X), MathF.Abs(ndc.Y)) - 1f;
        _fade = MathUtil.Clamp(1f - offscreen, 0f, 1f);

        // No rays once the sun has set below the horizon.
        float t = MathUtil.Clamp((-sun.Direction.Y + 0.02f) / 0.08f, 0f, 1f);
        _fade *= t * t * (3f - 2f * t);

        // An overcast sky hides the sun: no rays
        float storm = MathUtil.Clamp(context.Scene.Storm / 0.6f, 0f, 1f);
        _fade *= 1f - storm * storm * (3f - 2f * storm);
        return _fade > 0f && Intensity > 0f;
    }

    public override void Apply(PostContext context, int input)
    {
        _mask ??= LoadShader(context, "fx_sun_mask");
        _blur ??= LoadShader(context, "fx_sun_blur");
        _add ??= LoadShader(context, "fx_add");
        EnsureTargets(context);

        var sun = context.Scene.Sun!;

        // 1. Occlusion mask at half resolution.
        _maskTarget!.Bind();
        _mask.Use();
        _mask.SetInt("uInput", 0);
        _mask.SetInt("uDepth", 1);
        _mask.SetVector2("uSunPos", _sunUv);
        _mask.SetFloat("uAspect", (float)context.Width / context.Height);
        // Settings are kept in a range where the rays always look like light: a negative weight, a huge disc or a decay
        // above 1 would otherwise produce negative or exploding values (magenta/yellow blotches after tone mapping).
        _mask.SetFloat("uSunSize", Math.Clamp(SunSize, 0.005f, 0.1f));
        _mask.SetFloat("uDiscBrightness", Math.Clamp(DiscBrightness, 0f, 20f));
        _mask.SetInt("uCloudShape", 5);
        if (context.Scene.Skybox?.Physical?.Clouds is { } clouds)
        {
            clouds.SetOcclusionUniforms(_mask);
            var camera = context.Camera;
            _mask.SetMatrix4("uInvViewProj", Matrix4.Invert(camera.ViewMatrix * camera.ProjectionMatrix));
            _mask.SetVector3("uCameraPos", camera.Position);
        }
        else _mask.SetBool("uCloudShadows", false);
        context.BindTexture(TextureUnit.Texture1, context.DepthTexture);
        context.BindTexture(TextureUnit.Texture0, input);
        context.DrawQuad();

        // 2. Radial blur towards the sun.
        _rayTarget!.Bind();
        _blur.Use();
        _blur.SetInt("uInput", 0);
        _blur.SetVector2("uSunPos", _sunUv);
        _blur.SetFloat("uDensity", Math.Clamp(Density, 0f, 1.2f));
        _blur.SetFloat("uDecay", Math.Clamp(Decay, 0.8f, 0.99f));
        _blur.SetFloat("uWeight", Math.Clamp(Weight, 0f, 0.08f));
        context.BindTexture(TextureUnit.Texture0, _maskTarget.ColorTexture);
        context.DrawQuad();

        // 3. Add the tinted rays over the scene.
        context.BindOutput();
        _add.Use();
        _add.SetInt("uInput", 0);
        _add.SetInt("uOther", 1);
        _add.SetFloat("uIntensity", Math.Clamp(Intensity, 0f, 2f) * _fade);
        // The colour of the sunlight, not its strength: the rays' brightness is set by Intensity alone
        var color = sun.LightColor;
        float peak = MathF.Max(color.X, MathF.Max(color.Y, MathF.Max(color.Z, 1e-4f)));
        _add.SetVector3("uTint", color / peak);
        context.BindTexture(TextureUnit.Texture1, _rayTarget.ColorTexture);
        context.BindTexture(TextureUnit.Texture0, input);
        context.DrawQuad();
    }

    private void EnsureTargets(PostContext context)
    {
        int w = Math.Max(1, context.Width / 2), h = Math.Max(1, context.Height / 2);
        _maskTarget ??= Framebuffer.CreateColor(w, h, 0, hdr: true, DepthMode.None);
        _rayTarget ??= Framebuffer.CreateColor(w, h, 0, hdr: true, DepthMode.None);
        _maskTarget.Resize(w, h);
        _rayTarget.Resize(w, h);
    }

    public override void Dispose()
    {
        _maskTarget?.Dispose();
        _rayTarget?.Dispose();
        base.Dispose();
    }
}
