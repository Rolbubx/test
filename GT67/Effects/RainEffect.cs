using GT67.Core;
using OpenTK.Mathematics;

namespace GT67.Effects;

/// <summary>
/// Draws the rain of <see cref="Scene.Rain"/>: falling streaks in layers around the camera (hidden behind anything
/// closer than they are), splashes on the ground and a slightly dimmed look. It has no settings of its own;
/// set them on the <see cref="Rain"/> object. Without <c>scene.Rain</c> it does nothing.
/// </summary>
public sealed class RainEffect : SinglePassEffect
{
    protected override string ShaderName => "fx_rain";

    public override bool ShouldRun(PostContext context) => context.Scene.RainAmount > 0f;

    protected override void SetUniforms(Shader shader, PostContext context)
    {
        var camera = context.Camera;
        var rain = context.Scene.Rain!;

        shader.SetMatrix4("uInvViewProj", Matrix4.Invert(camera.ViewMatrix * camera.ProjectionMatrix));
        shader.SetVector3("uCameraPos", camera.Position);
        shader.SetVector3("uFront", camera.Front);
        shader.SetFloat("uNear", camera.Near);
        shader.SetFloat("uFar", camera.Far);
        shader.SetFloat("uTime", context.Time);
        shader.SetFloat("uIntensity", rain.Amount);
        shader.SetFloat("uSpeed", rain.Speed);
        shader.SetFloat("uLength", MathF.Max(rain.Length, 0.05f));
        shader.SetFloat("uSlant", rain.Slant);
        shader.SetFloat("uBrightness", rain.Brightness);
        shader.SetFloat("uDarken", rain.Darken);
        shader.SetFloat("uSplashes", context.Quality >= 2 ? 0f : rain.Splashes);
        shader.SetInt("uLayers", context.Quality switch { 0 => 5, 1 => 4, 2 => 3, _ => 2 });
        shader.SetVector2("uTexel", new Vector2(1f / context.Width, 1f / context.Height));
        shader.SetVector3("uSunDir", context.Scene.Sun is { IsEmitting: true } s ? s.DirectionToSun : Vector3.UnitY);

        // Rain catches the sky's light: use the sun colour when there is one, a cool grey otherwise.
        var tint = context.Scene.Sun is { IsEmitting: true } sun ? sun.LightColor : new Vector3(0.6f, 0.65f, 0.75f);
        shader.SetVector3("uTint", Vector3.Lerp(new Vector3(0.7f, 0.75f, 0.85f), tint, 0.4f) * MathF.Max(context.Scene.LightDim, 0.35f));
    }
}
