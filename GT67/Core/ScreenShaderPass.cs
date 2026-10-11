using GT67.Utils;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>Runs one screen shader of the scene's Volume (<see cref="PostProfile.ScreenShaders"/>) as a step of the post-processing chain.</summary>
internal sealed class ScreenShaderPass : PostEffect
{
    private const int FirstTextureUnit = 2;   // 0 = the picture, 1 = the depth
    private const int LastTextureUnit = 9;

    private readonly Func<string, Texture> _texture;

    public ScreenShaderPass(Func<string, Texture> texture) => _texture = texture;

    /// <summary>The .shader file (with its compiled program) and the Volume entry with its settings.</summary>
    public CustomShader Source { get; set; } = null!;
    public ScreenShaderUse Use { get; set; } = null!;

    public override string Name => Use.Name;
    public override PostStage Stage => Source.Stage;

    public override void Apply(PostContext context, int input)
    {
        var program = Source.Program!;
        program.Use();
        context.BindTexture(TextureUnit.Texture0, input);
        program.SetInt("uInput", 0);
        context.BindTexture(TextureUnit.Texture1, context.DepthTexture);
        program.SetInt("uDepthTexture", 1);
        program.SetVector2("uResolution", new Vector2(context.Width, context.Height));
        program.SetFloat("uTime", context.Time);
        program.SetFloat("uNear", context.Camera.Near);
        program.SetFloat("uFar", context.Camera.Far);
        program.SetMatrix4("uInvViewProjection", (context.Camera.ViewMatrix * context.Camera.ProjectionMatrix).Inverted());
        program.SetVector3("uCameraPosition", context.Camera.Position);
        var sun = context.Scene.Sun;
        program.SetVector3("uSunDirection", sun?.DirectionToSun ?? Vector3.UnitY);
        program.SetVector3("uSunColor", sun is { IsEmitting: true } ? sun.LightColor * context.Scene.LightDim : Vector3.Zero);

        int unit = FirstTextureUnit;
        foreach (var param in Source.Params)
        {
            if (param.Kind == ShaderParamKind.Texture)
            {
                if (unit > LastTextureUnit) continue;
                _texture(Use.GetTexture(param)).Bind(TextureUnit.Texture0 + unit);
                program.SetInt(param.Name, unit++);
                continue;
            }
            var value = Use.Get(param);
            switch (param.Kind)
            {
                case ShaderParamKind.Float: program.SetFloat(param.Name, value.X); break;
                case ShaderParamKind.Bool: program.SetBool(param.Name, value.X > 0.5f); break;
                case ShaderParamKind.Vec2: program.SetVector2(param.Name, new Vector2(value.X, value.Y)); break;
                case ShaderParamKind.Color:
                    // After tone mapping the picture is in display colours, like the colour picker; before it, linear light.
                    var color = new Color4(value.X, value.Y, value.Z, 1f);
                    program.SetVector3(param.Name, Stage == PostStage.Hdr ? MathUtil.ToLinear(color) : new Vector3(value.X, value.Y, value.Z));
                    break;
                default: program.SetVector3(param.Name, new Vector3(value.X, value.Y, value.Z)); break;
            }
        }
        context.DrawQuad();
    }
}
