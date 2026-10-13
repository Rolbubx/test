using GT67.Core;
using GT67.Utils;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GT67.Effects;

/// <summary>
/// The sun's light shafts ("god rays"), in two layers:
/// <list type="bullet">
/// <item><b>Volumetric shafts</b>: every pixel marches along its view ray through the air and adds the sunlight the
/// lit air scatters towards the camera: a ground haze around the player, where the shadows of objects cut beams, and
/// the thin air up to the clouds, where the clouds cut the big ones. They are real beams in the world: seen in every
/// direction (brightest towards the sun), even with the sun behind the camera. Marched at half resolution with a
/// different noise every frame, accumulated over frames (reprojection), smoothed, then upsampled along depth edges.</item>
/// <item><b>Glare</b>: while the sun is on screen, the sky around its disc is blurred outwards from it (the dazzle of
/// looking straight at the sun), blocked by anything in front of it.</item>
/// </list>
/// </summary>
public sealed class SunRaysEffect : PostEffect
{
    private Shader? _mask, _blur, _shafts, _temporal, _shaftBlur, _lanes, _composite;
    private Framebuffer? _maskTarget, _rayTarget, _shaftA, _shaftB, _lanesTarget;
    private readonly Framebuffer?[] _history = new Framebuffer?[2];
    private int _historyIndex;
    private bool _historyValid;
    private Matrix4 _prevViewProj;
    private Vector3 _prevCameraPos;
    private Vector2 _sunUv;
    private float _screenFade, _sunFade;
    private int _frame;

    /// <summary>
    /// On: volumetric shafts in the air, seen in every direction, plus the glare. Off: the normal (screen-space) sun
    /// rays only, streaming from the sun's disc while it is on screen; cheaper.
    /// </summary>
    public bool Volumetric { get; set; } = true;

    // ---- glare (screen space, while the sun is on screen) -------------------------------------------

    /// <summary>Strength of the glare streaming from the sun's disc while it is on screen.</summary>
    public float Intensity { get; set; } = 0.4f;

    /// <summary>How far the glare reaches towards the sun (1 = all the way).</summary>
    public float Density { get; set; } = 0.95f;

    /// <summary>Per-step falloff along each glare streak (closer to 1 = longer streaks).</summary>
    public float Decay { get; set; } = 0.97f;

    public float Weight { get; set; } = 0.03f;

    /// <summary>Radius of the bright sun disc, in screen-height units.</summary>
    public float SunSize { get; set; } = 0.035f;

    public float DiscBrightness { get; set; } = 6f;

    // ---- volumetric shafts (in the world, seen everywhere) ------------------------------------------

    /// <summary>Strength of the volumetric shafts (0 = off).</summary>
    public float Shafts { get; set; } = 1f;

    /// <summary>
    /// Density of the haze around the player, per metre: the air in which the shadows of trees, walls and other objects
    /// cut visible beams (more = thicker beams, and more haze over the distance; 0 = none).
    /// </summary>
    public float GroundHaze { get; set; } = 0.006f;

    /// <summary>How quickly the ground haze thins above the camera: it drops to about a third every this many metres.</summary>
    public float GroundHazeHeight { get; set; } = 20f;

    /// <summary>How far from the camera the ground haze reaches, in metres (it fades out over the last half).</summary>
    public float GroundHazeRange { get; set; } = 40f;

    /// <summary>How patchy the ground haze is: denser and thinner patches drifting in the wind (0 = even, 1 = very patchy).</summary>
    public float HazeNoise { get; set; } = 0.5f;

    /// <summary>Size of the haze patches, in metres.</summary>
    public float HazeNoiseSize { get; set; } = 14f;

    /// <summary>The ground haze dims what lies behind it, as real haze does (off: it only adds light).</summary>
    public bool HazeDimsScene { get; set; } = true;

    /// <summary>Accumulate the shafts over several frames: much smoother beams for the same cost (may trail a little in fast motion).</summary>
    public bool Temporal { get; set; } = true;

    /// <summary>How much the thin air up to the clouds scatters the sunlight, per metre at height 0 (the big beams between clouds).</summary>
    public float Haze { get; set; } = 0.0003f;

    /// <summary>How quickly the haze thins with height, per metre (0.0008 keeps most of it below ~1.2 km, under the clouds).</summary>
    public float HeightFalloff { get; set; } = 0.0008f;

    /// <summary>How far the shafts are traced, in metres; the sky counts as this far away.</summary>
    public float MaxDistance { get; set; } = 6000f;

    /// <summary>Samples along each view ray (more = finer shadows in the beams, slower).</summary>
    public int Steps { get; set; } = 32;

    /// <summary>How strongly the haze scatters light onwards, towards the sun (0 = evenly, 0.9 = tight glow around it).</summary>
    public float Forward { get; set; } = 0.55f;

    /// <summary>Share of the light scattered evenly in every direction: the shafts stay visible looking away from the sun.</summary>
    public float Spread { get; set; } = 0.35f;

    /// <summary>Contrast of the big beams between the clouds: lanes of lit air brighter than the plain sky around them (0..1).</summary>
    public float ShadowContrast { get; set; } = 0.85f;

    /// <summary>The clouds' shadows cut beams into the air (big rays through the gaps between clouds).</summary>
    public bool CloudShafts { get; set; } = true;

    public override bool ShouldRun(PostContext context)
    {
        var sun = context.Scene.Sun;
        if (sun is not { IsEmitting: true }) return false;

        // No shafts once the sun has set below the horizon, and none under an overcast sky.
        float t = MathUtil.Clamp((-sun.Direction.Y + 0.02f) / 0.08f, 0f, 1f);
        _sunFade = t * t * (3f - 2f * t);
        float storm = MathUtil.Clamp(context.Scene.Storm / 0.6f, 0f, 1f);
        _sunFade *= 1f - storm * storm * (3f - 2f * storm);

        // The glare needs the sun on screen: it fades as the sun leaves it. The sun is infinitely far away.
        _screenFade = 0f;
        var camera = context.Camera;
        var clip = Vector4.TransformRow(new Vector4(camera.Position - sun.Direction * 1000f, 1f), camera.ViewMatrix * camera.ProjectionMatrix);
        if (clip.W > 0.001f)
        {
            var ndc = clip.Xy / clip.W;
            _sunUv = ndc * 0.5f + new Vector2(0.5f);
            float offscreen = MathF.Max(MathF.Abs(ndc.X), MathF.Abs(ndc.Y)) - 1f;
            _screenFade = MathUtil.Clamp(1f - offscreen, 0f, 1f);
        }

        return _sunFade > 0f && ((Volumetric && Shafts > 0f) || (Intensity > 0f && _screenFade > 0f));
    }

    public override void Apply(PostContext context, int input)
    {
        _mask ??= LoadShader(context, "fx_sun_mask");
        _blur ??= LoadShader(context, "fx_sun_blur");
        _shafts ??= LoadShader(context, "fx_sun_shafts");
        _temporal ??= LoadShader(context, "fx_sun_shafts_temporal");
        _lanes ??= LoadShader(context, "fx_sun_lanes");
        _shaftBlur ??= LoadShader(context, "fx_sun_shafts_blur");
        _composite ??= LoadShader(context, "fx_sun_composite");
        EnsureTargets(context);

        var scene = context.Scene;
        var sun = scene.Sun!;
        var camera = context.Camera;
        var clouds = scene.Skybox?.ActivePhysical?.Clouds;
        var invViewProj = Matrix4.Invert(camera.ViewMatrix * camera.ProjectionMatrix);

        bool glare = Intensity > 0f && _screenFade > 0f;
        if (glare) DrawGlare(context, input, clouds, invViewProj);
        else Clear(_rayTarget!);

        bool shafts = Volumetric && Shafts > 0f;
        if (shafts) DrawShafts(context, sun, clouds, invViewProj);
        else
        {
            Clear(_shaftA!);
            _historyValid = false;
        }

        // Both over the picture, in the sun's colour
        context.BindOutput();
        _composite.Use();
        _composite.SetInt("uInput", 0);
        _composite.SetInt("uShafts", 1);
        _composite.SetInt("uGlare", 2);
        _composite.SetInt("uDepth", 3);
        var light = sun.LightColor * scene.LightDim;
        float strength = Math.Clamp(Shafts, 0f, 5f);
        _composite.SetVector3("uShaftColor", light * (strength * _sunFade));
        // The haze glows with the sky's light (never black in the shade): the ambient light a white surface gets
        _composite.SetVector3("uAmbientColor", MathUtil.ToLinear(scene.AmbientColor) * scene.AmbientIntensity * scene.AmbientDim * strength);
        _composite.SetFloat("uExtinction", shafts && HazeDimsScene ? MathF.Min(strength, 1f) : 0f);
        _composite.SetFloat("uDarken", shafts ? Math.Clamp(ShadowContrast, 0f, 1f) * MathF.Min(strength, 1f) * _sunFade : 0f);
        _composite.SetVector3("uBeamColor", light * 0.6f);
        _composite.SetInt("uLanes", 4);
        _composite.SetVector3("uSunDir", sun.DirectionToSun);
        _composite.SetMatrix4("uInvViewProj", invViewProj);
        _composite.SetVector3("uCameraPos", camera.Position);
        _composite.SetVector3("uFront", camera.Front);
        _composite.SetFloat("uNear", camera.Near);
        _composite.SetFloat("uFar", camera.Far);
        _composite.SetFloat("uMaxDistance", Math.Clamp(MaxDistance, 10f, 50000f));
        float peak = MathF.Max(light.X, MathF.Max(light.Y, MathF.Max(light.Z, 1e-4f)));
        // Alone (not volumetric), the glare is the whole effect: as strong as the classic sun rays
        float glareStrength = Math.Clamp(Intensity, 0f, 2f) * (shafts ? 1f : 1.5f);
        _composite.SetVector3("uGlareColor", light / peak * (glareStrength * _screenFade * _sunFade));
        context.BindTexture(TextureUnit.Texture3, context.DepthTexture);
        context.BindTexture(TextureUnit.Texture4, _lanesTarget!.ColorTexture);
        context.BindTexture(TextureUnit.Texture1, _shaftA!.ColorTexture);
        context.BindTexture(TextureUnit.Texture2, _rayTarget!.ColorTexture);
        context.BindTexture(TextureUnit.Texture0, input);
        context.DrawQuad();
    }

    /// <summary>The glare: what of the sun and the bright sky around it is not hidden, blurred outwards from the sun.</summary>
    private void DrawGlare(PostContext context, int input, CloudLayer? clouds, Matrix4 invViewProj)
    {
        _maskTarget!.Bind();
        _mask!.Use();
        _mask.SetInt("uInput", 0);
        _mask.SetInt("uDepth", 1);
        _mask.SetVector2("uSunPos", _sunUv);
        _mask.SetFloat("uAspect", (float)context.Width / context.Height);
        // Settings are kept in a range where the glare always looks like light: a negative weight, a huge disc or a
        // decay above 1 would otherwise produce negative or exploding values (magenta/yellow blotches after tone mapping).
        _mask.SetFloat("uSunSize", Math.Clamp(SunSize, 0.005f, 0.1f));
        _mask.SetFloat("uDiscBrightness", Math.Clamp(DiscBrightness, 0f, 20f));
        _mask.SetInt("uCloudShape", 5);
        if (clouds != null)
        {
            clouds.SetOcclusionUniforms(_mask);
            _mask.SetMatrix4("uInvViewProj", invViewProj);
            _mask.SetVector3("uCameraPos", context.Camera.Position);
        }
        else _mask.SetBool("uCloudShadows", false);
        context.BindTexture(TextureUnit.Texture1, context.DepthTexture);
        context.BindTexture(TextureUnit.Texture0, input);
        context.DrawQuad();

        _rayTarget!.Bind();
        _blur!.Use();
        _blur.SetInt("uInput", 0);
        _blur.SetVector2("uSunPos", _sunUv);
        _blur.SetFloat("uDensity", Math.Clamp(Density, 0f, 1.2f));
        _blur.SetFloat("uDecay", Math.Clamp(Decay, 0.8f, 0.99f));
        _blur.SetFloat("uWeight", Math.Clamp(Weight, 0f, 0.08f));
        context.BindTexture(TextureUnit.Texture0, _maskTarget.ColorTexture);
        context.DrawQuad();
    }

    /// <summary>
    /// The volumetric shafts: marched at half resolution into A, blended with last frame's result into the history,
    /// then smoothed history -> B -> A for the composite.
    /// </summary>
    private void DrawShafts(PostContext context, Sun sun, CloudLayer? clouds, Matrix4 invViewProj)
    {
        var camera = context.Camera;
        _shaftA!.Bind();
        _shafts!.Use();
        _shafts.SetInt("uDepth", 1);
        _shafts.SetMatrix4("uInvViewProj", invViewProj);
        _shafts.SetVector3("uCameraPos", camera.Position);
        _shafts.SetVector3("uFront", camera.Front);
        _shafts.SetFloat("uNear", camera.Near);
        _shafts.SetFloat("uFar", camera.Far);
        _shafts.SetFloat("uFrame", _frame++ % 64);
        _shafts.SetVector3("uSunDir", sun.DirectionToSun);
        _shafts.SetFloat("uGroundHaze", Math.Clamp(GroundHaze, 0f, 0.5f));
        _shafts.SetFloat("uGroundHeight", Math.Clamp(GroundHazeHeight, 0.5f, 5000f));
        _shafts.SetFloat("uGroundRange", Math.Clamp(GroundHazeRange, 5f, 5000f));
        float noiseScale = 1f / Math.Clamp(HazeNoiseSize, 0.5f, 1000f);
        var wind = clouds != null ? clouds.WindDirection * MathF.Max(clouds.WindSpeed * 0.1f, 0.6f) : new Vector2(1f, 0.3f);
        _shafts.SetFloat("uNoise", Math.Clamp(HazeNoise, 0f, 1f));
        _shafts.SetFloat("uNoiseScale", noiseScale);
        _shafts.SetVector2("uWind", wind * noiseScale);
        _shafts.SetFloat("uTime", context.Time);
        _shafts.SetFloat("uSkyShare", 0.72f);   // as bright as before the ground haze was added
        _shafts.SetFloat("uHaze", Math.Clamp(Haze, 0f, 0.05f));
        _shafts.SetFloat("uHeightFalloff", MathF.Max(HeightFalloff, 1e-5f));
        _shafts.SetFloat("uMaxDistance", Math.Clamp(MaxDistance, 10f, 50000f));
        _shafts.SetInt("uSteps", Math.Clamp((int)(Steps * (context.Quality switch { 0 => 1f, 1 => 0.75f, 2 => 0.55f, _ => 0.4f })), 8, 128));
        _shafts.SetFloat("uForward", Math.Clamp(Forward, 0f, 0.95f));
        _shafts.SetFloat("uSpread", Math.Clamp(Spread, 0f, 1f));
        _shafts.SetBool("uCloudShafts", CloudShafts);

        bool shadows = context.ShadowTexture != 0;
        _shafts.SetBool("uShadowsEnabled", shadows);
        if (shadows)
        {
            _shafts.SetInt("uShadowMap", 2);
            _shafts.SetMatrix4("uLightSpace", context.ShadowMatrix);
            context.BindTexture(TextureUnit.Texture2, context.ShadowTexture);
        }
        _shafts.SetInt("uCloudShape", 5);
        if (clouds != null) clouds.SetOcclusionUniforms(_shafts);
        else _shafts.SetBool("uCloudShadows", false);
        context.BindTexture(TextureUnit.Texture1, context.DepthTexture);
        context.DrawQuad();

        // Blend with last frame's result, found where the same point of the world was on screen then
        var history = _history[_historyIndex]!;
        var previous = _history[1 - _historyIndex]!;
        history.Bind();
        _temporal!.Use();
        _temporal.SetInt("uCurrent", 0);
        _temporal.SetInt("uHistory", 1);
        _temporal.SetMatrix4("uInvViewProj", invViewProj);
        _temporal.SetMatrix4("uPrevViewProj", _prevViewProj);
        _temporal.SetVector3("uCameraPos", camera.Position);
        _temporal.SetVector3("uPrevCameraPos", _prevCameraPos);
        _temporal.SetFloat("uBlend", Temporal && _historyValid ? 0.9f : 0f);
        context.BindTexture(TextureUnit.Texture1, previous.ColorTexture);
        context.BindTexture(TextureUnit.Texture0, _shaftA.ColorTexture);
        context.DrawQuad();
        _prevViewProj = camera.ViewMatrix * camera.ProjectionMatrix;
        _prevCameraPos = camera.Position;
        _historyValid = true;
        _historyIndex = 1 - _historyIndex;

        // Smooth what grain is left, without bleeding across depth edges (the history keeps the unsmoothed result)
        _shaftBlur!.Use();
        _shaftBlur.SetInt("uInput", 0);
        _shaftB!.Bind();
        _shaftBlur.SetVector2("uStep", new Vector2(1f / _shaftA.Width, 0f));
        context.BindTexture(TextureUnit.Texture0, history.ColorTexture);
        context.DrawQuad();
        _shaftA.Bind();
        _shaftBlur.SetVector2("uStep", new Vector2(0f, 1f / _shaftA.Height));
        context.BindTexture(TextureUnit.Texture0, _shaftB.ColorTexture);
        context.DrawQuad();

        // The far pixels only, mipmapped: the composite compares every far pixel with the average of the far pixels
        // around it (the lanes of the big beams), without the floor or nearby objects leaking into the sky
        _lanesTarget!.Bind();
        _lanes!.Use();
        _lanes.SetInt("uShafts", 0);
        context.BindTexture(TextureUnit.Texture0, _shaftA.ColorTexture);
        context.DrawQuad();
        GL.BindTexture(TextureTarget.Texture2D, _lanesTarget.ColorTexture);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
    }

    private static void Clear(Framebuffer target)
    {
        target.Bind();
        GL.ClearColor(0f, 0f, 0f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit);
    }

    private void EnsureTargets(PostContext context)
    {
        int w = Math.Max(1, context.Width / 2), h = Math.Max(1, context.Height / 2);
        _maskTarget ??= Framebuffer.CreateColor(w, h, 0, hdr: true, DepthMode.None);
        _rayTarget ??= Framebuffer.CreateColor(w, h, 0, hdr: true, DepthMode.None);
        _shaftA ??= Framebuffer.CreateColor(w, h, 0, hdr: true, DepthMode.None);
        _shaftB ??= Framebuffer.CreateColor(w, h, 0, hdr: true, DepthMode.None);
        _lanesTarget ??= Framebuffer.CreateColor(w, h, 0, hdr: true, DepthMode.None);
        _lanesTarget.Resize(w, h);
        for (int i = 0; i < 2; i++)
        {
            _history[i] ??= Framebuffer.CreateColor(w, h, 0, hdr: true, DepthMode.None);
            if (_history[i]!.Width != w || _history[i]!.Height != h) _historyValid = false;
            _history[i]!.Resize(w, h);
        }
        _maskTarget.Resize(w, h);
        _rayTarget.Resize(w, h);
        _shaftA.Resize(w, h);
        _shaftB.Resize(w, h);
    }

    public override void Dispose()
    {
        _maskTarget?.Dispose();
        _rayTarget?.Dispose();
        _shaftA?.Dispose();
        _shaftB?.Dispose();
        _lanesTarget?.Dispose();
        foreach (var history in _history) history?.Dispose();
        base.Dispose();
    }
}
