using GT67.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GT67.Effects;

/// <summary>Glow around bright areas: threshold, progressive downsample, tent-filter upsample, add back.</summary>
public sealed class BloomEffect : PostEffect
{
    private Shader? _down, _up, _add;
    private Framebuffer[] _mips = Array.Empty<Framebuffer>();
    private int _width, _height, _builtIterations;

    /// <summary>Linear brightness above which pixels start to glow.</summary>
    public float Threshold { get; set; } = 1.0f;

    /// <summary>Width of the soft transition around the threshold.</summary>
    public float SoftKnee { get; set; } = 0.5f;

    public float Intensity { get; set; } = 0.5f;

    /// <summary>Spread of the glow (multiplies the upsample filter width).</summary>
    public float Radius { get; set; } = 1f;

    /// <summary>Number of downsample steps; more steps give a wider glow.</summary>
    public int Iterations { get; set; } = 5;

    public override void Apply(PostContext context, int input)
    {
        _down ??= LoadShader(context, "fx_bloom_down");
        _up ??= LoadShader(context, "fx_bloom_up");
        _add ??= LoadShader(context, "fx_add");
        EnsureTargets(context);
        if (_mips.Length == 0)
        {
            // Too small to build a chain; nothing to add.
            context.BindOutput();
            return;
        }

        // Downsample (the first step also applies the brightness threshold).
        GL.Disable(EnableCap.Blend);
        int source = input;
        int sourceWidth = context.Width, sourceHeight = context.Height;
        for (int i = 0; i < _mips.Length; i++)
        {
            _mips[i].Bind();
            _down.Use();
            _down.SetInt("uInput", 0);
            _down.SetVector2("uTexel", new Vector2(1f / sourceWidth, 1f / sourceHeight));
            _down.SetBool("uPrefilter", i == 0);
            _down.SetFloat("uThreshold", Threshold);
            _down.SetFloat("uKnee", MathF.Max(SoftKnee, 0.001f));
            context.BindTexture(TextureUnit.Texture0, source);
            context.DrawQuad();

            source = _mips[i].ColorTexture;
            sourceWidth = _mips[i].Width;
            sourceHeight = _mips[i].Height;
        }

        // Upsample, accumulating each level into the next larger one.
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);
        for (int i = _mips.Length - 2; i >= 0; i--)
        {
            _mips[i].Bind();
            _up.Use();
            _up.SetInt("uInput", 0);
            _up.SetVector2("uTexel", new Vector2(1f / _mips[i + 1].Width, 1f / _mips[i + 1].Height));
            _up.SetFloat("uRadius", Radius);
            context.BindTexture(TextureUnit.Texture0, _mips[i + 1].ColorTexture);
            context.DrawQuad();
        }
        GL.Disable(EnableCap.Blend);

        // Composite over the original image.
        context.BindOutput();
        _add.Use();
        _add.SetInt("uInput", 0);
        _add.SetInt("uOther", 1);
        _add.SetFloat("uIntensity", Intensity);
        _add.SetVector3("uTint", Vector3.One);
        context.BindTexture(TextureUnit.Texture1, _mips[0].ColorTexture);
        context.BindTexture(TextureUnit.Texture0, input);
        context.DrawQuad();
    }

    private void EnsureTargets(PostContext context)
    {
        int iterations = Math.Clamp(Iterations, 1, 8);
        if (_mips.Length > 0 && _width == context.Width && _height == context.Height && _builtIterations == iterations)
            return;

        foreach (var mip in _mips) mip.Dispose();

        var list = new List<Framebuffer>();
        int w = context.Width / 2, h = context.Height / 2;
        for (int i = 0; i < iterations && w >= 4 && h >= 4; i++)
        {
            list.Add(Framebuffer.CreateColor(w, h, 0, hdr: true, DepthMode.None));
            w /= 2;
            h /= 2;
        }
        _mips = list.ToArray();
        _width = context.Width;
        _height = context.Height;
        _builtIterations = iterations;
    }

    public override void Dispose()
    {
        foreach (var mip in _mips) mip.Dispose();
        _mips = Array.Empty<Framebuffer>();
        base.Dispose();
    }
}
