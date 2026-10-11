using GT67.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GT67.Effects;

/// <summary>
/// Real volumetric fog: every pixel is ray-marched through drifting, ground-hugging fog that is lit by the
/// sky and by the sun, so the sun's shadow map carves light shafts into it and the clouds can block them.
/// The amount comes from <see cref="Scene.FogDensity"/> (<see cref="Scene.Fog"/> plus the storm's mist).
/// While this effect runs, the shaders' simple distance fog switches itself off.
/// </summary>
public sealed class VolumetricFogEffect : SinglePassEffect
{
    private int _frame;

    protected override string ShaderName => "fx_volfog";

    /// <summary>Multiplies the scene's fog density.</summary>
    public float Strength { get; set; } = 1f;

    /// <summary>How quickly the fog thins with height (per metre). 0.012 puts most of it in the lowest ~80 m.</summary>
    public float HeightFalloff { get; set; } = 0.012f;

    /// <summary>Marching stops here; the sky counts as this far away.</summary>
    public float MaxDistance { get; set; } = 500f;

    /// <summary>Samples along each ray. More = smoother, slower.</summary>
    public int Steps { get; set; } = 14;

    /// <summary>Forward scattering of the fog droplets: higher gives brighter shafts looking towards the sun.</summary>
    public float Anisotropy { get; set; } = 0.55f;

    public override bool ShouldRun(PostContext context)
    {
        bool on = context.Scene.FogDensity * Strength > 1e-5f;
        if (on) context.Scene.VolumetricFogTick = Environment.TickCount64;
        return on;
    }

    protected override void SetUniforms(Shader shader, PostContext context)
    {
        var scene = context.Scene;
        var camera = context.Camera;

        shader.SetMatrix4("uInvViewProj", Matrix4.Invert(camera.ViewMatrix * camera.ProjectionMatrix));
        shader.SetVector3("uCameraPos", camera.Position);
        shader.SetVector3("uFront", camera.Front);
        shader.SetFloat("uNear", camera.Near);
        shader.SetFloat("uFar", camera.Far);
        shader.SetFloat("uTime", context.Time);
        shader.SetFloat("uFrame", _frame++ % 64);

        shader.SetFloat("uDensity", scene.FogDensity * Strength);
        shader.SetFloat("uHeightFalloff", MathF.Max(HeightFalloff, 1e-4f));
        shader.SetFloat("uMaxDistance", MathF.Max(MaxDistance, 10f));
        shader.SetInt("uSteps", Math.Clamp((int)(Steps * (context.Quality switch { 0 => 1f, 1 => 0.7f, 2 => 0.5f, _ => 0.35f })), 4, 128));
        shader.SetVector3("uFogColor", scene.FogColor);
        shader.SetFloat("uAnisotropy", Math.Clamp(Anisotropy, 0f, 0.95f));

        var sun = scene.Sun;
        bool sunLit = sun is { IsEmitting: true };
        shader.SetVector3("uSunDir", sunLit ? sun!.DirectionToSun : Vector3.UnitY);
        shader.SetVector3("uSunColor", sunLit ? sun!.LightColor * scene.LightDim * 0.6f : Vector3.Zero);

        var clouds = scene.Skybox?.ActivePhysical?.Clouds;
        shader.SetVector2("uWind", clouds != null ? clouds.WindDirection * clouds.WindSpeed * 0.1f : new Vector2(1f, 0.25f));

        // Sun shadow map (unit 2) and the cloud deck (unit 5)
        bool shadows = context.ShadowTexture != 0;
        shader.SetBool("uShadowsEnabled", shadows);
        if (shadows)
        {
            shader.SetInt("uShadowMap", 2);
            shader.SetMatrix4("uLightSpace", context.ShadowMatrix);
            context.BindTexture(TextureUnit.Texture2, context.ShadowTexture);
        }

        shader.SetInt("uCloudShape", 5);
        if (clouds != null) clouds.SetOcclusionUniforms(shader);
        else shader.SetBool("uCloudShadows", false);
    }
}
