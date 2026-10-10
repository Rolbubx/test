using OpenTK.Graphics.OpenGL4;

namespace GT67.Core;

/// <summary>
/// Base for effects that are one fullscreen shader. The shader gets the input on unit 0 (<c>uInput</c>)
/// and the scene depth on unit 1 (<c>uDepth</c>); subclasses set their own uniforms.
/// </summary>
public abstract class SinglePassEffect : PostEffect
{
    private Shader? _shader;

    /// <summary>Fragment shader file name without extension, e.g. "fx_vignette".</summary>
    protected abstract string ShaderName { get; }

    protected abstract void SetUniforms(Shader shader, PostContext context);

    public override void Apply(PostContext context, int input)
    {
        _shader ??= LoadShader(context, ShaderName);

        _shader.Use();
        _shader.SetInt("uInput", 0);
        _shader.SetInt("uDepth", 1);
        _shader.SetVector2("uTexel", new OpenTK.Mathematics.Vector2(1f / context.Width, 1f / context.Height));
        _shader.SetFloat("uAspect", (float)context.Width / context.Height);
        SetUniforms(_shader, context);

        context.BindTexture(TextureUnit.Texture1, context.DepthTexture);
        context.BindTexture(TextureUnit.Texture0, input);
        context.DrawQuad();
    }
}
