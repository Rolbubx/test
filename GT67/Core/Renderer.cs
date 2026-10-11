using GT67.Utils;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>
/// Owns the shaders and render targets and runs the frame:
/// shadow pass, opaque, skybox, sorted transparent objects, MSAA resolve, then tone mapping to the screen.
/// </summary>
public sealed class Renderer : IDisposable
{
    private static readonly string[] PointLightFields = BuildPointLightFields();
    private static readonly string[] SplashFields = Enumerable.Range(0, Water.MaxSplashes).Select(i => $"uSplashes[{i}]").ToArray();
    private static readonly string[] CausticFields = Enumerable.Range(0, MaxCausticWaters).Select(i => $"uCausticWater[{i}]").ToArray();

    /// <summary>How many bodies of water can throw caustics at once (must match MAX_CAUSTIC_WATERS in lit.frag).</summary>
    private const int MaxCausticWaters = 4;

    private readonly List<GameObject> _visible = new();
    private readonly List<GameObject> _opaque = new();
    private readonly List<GameObject> _transparent = new();
    private readonly List<GameObject> _overlay = new();
    private readonly List<(float distance, GameObject obj)> _sortBuffer = new();
    private readonly List<GameObject> _offscreenOpaque = new();   // outside the camera's view, but maybe in a reflection
    private readonly OcclusionCuller _occlusion;
    private Framebuffer _sceneBuffer = null!;
    private Framebuffer? _resolveBuffer;
    private Framebuffer? _refractionBuffer;
    private readonly Texture _rippleMap;
    private Framebuffer? _puddleBuffer;
    private bool _puddleReady;
    private bool _inMirror;

    // The main pass's frame settings, replayed on custom shader programs the first time each is used
    private Scene? _frameScene;
    private Matrix4 _frameView, _frameProjection;
    private Vector3 _frameViewPos;
    private bool _frameShadows;
    private Vector4 _frameClip;
    private readonly HashSet<Shader> _frameCustom = new();

    /// <summary>Custom shaders written in the GT67 shader language (.shader files); see <see cref="ShaderLibrary"/>.</summary>
    public ShaderLibrary Custom { get; }

    /// <summary>Resolution of the puddle mirror relative to the screen.</summary>
    public float PuddleScale { get; set; } = 1f;

    /// <summary>Turns the puddle mirror pass off (puddles then reflect a plain sky colour).</summary>
    public bool ReflectionsEnabled { get; set; } = true;
    private readonly List<Water> _activeWaters = new();
    private int _width, _height;

    public Shader Lit { get; }
    public Shader Sky { get; }
    public Shader Depth { get; }
    public Shader PostShader { get; }
    public Shader WaterShader { get; }
    public Shader GrassShader { get; }
    public PostProcess Post { get; }
    /// <summary>Quality level, 0 (best) to 3 (fastest). Lowers the fog, puddle mirrors, cloud march and rain layers.</summary>
    public int Quality { get; set; }

    /// <summary>Lets the renderer lower <see cref="Quality"/> by itself while the frame rate is low, and raise it again when there is room.</summary>
    public bool AdaptiveQuality { get; set; } = true;

    /// <summary>Smoothed time of one frame, in seconds.</summary>
    public float AverageFrameTime { get; private set; } = 1f / 60f;

    private float _sinceQualityChange = -3f;   // start-up (shader compiles, noise textures) does not count

    /// <summary>Called by the window with the time the last frame took; drives <see cref="AdaptiveQuality"/>.</summary>
    public void ReportFrameTime(float dt)
    {
        dt = Math.Clamp(dt, 0.001f, 0.1f);
        AverageFrameTime += (dt - AverageFrameTime) * 0.04f;
        _sinceQualityChange += dt;
        if (!AdaptiveQuality || _sinceQualityChange < 0f) return;

        if (AverageFrameTime > 0.024f && _sinceQualityChange > 1.5f && Quality < 3)
        {
            Quality++;
            _sinceQualityChange = 0f;
            Log.Info($"Frame rate low ({1f / AverageFrameTime:0} fps): quality lowered to {Quality}.");
        }
        else if (AverageFrameTime < 0.0185f && _sinceQualityChange > 20f && Quality > 0)
        {
            Quality--;
            _sinceQualityChange = 0f;
            Log.Info($"Frame rate fine ({1f / AverageFrameTime:0} fps): quality raised to {Quality}.");
        }
    }

    private float CloudQuality => Quality switch { 0 => 1f, 1 => 0.75f, 2 => 0.55f, _ => 0.4f };

    public ShadowMap Shadow { get; }

    public Color4 ClearColor { get; set; } = new(0.08f, 0.09f, 0.11f, 1f);
    public bool Wireframe { get; set; }
    public bool SkyboxEnabled { get; set; } = true;
    public bool ShadowsEnabled { get; set; } = true;
    public int MsaaSamples { get; private set; }

    /// <summary>Drawn again as a flat-colour wireframe outline (used for the selected object).</summary>
    public GameObject? Highlight { get; set; }
    public Color4 HighlightColor { get; set; } = new(1f, 0.65f, 0.1f, 1f);

    public int ObjectsDrawn { get; private set; }
    public int DrawCalls { get; private set; }

    /// <summary>Skip objects outside the camera's view (and, for shadows and reflections, outside those views).</summary>
    public bool FrustumCulling { get; set; } = true;

    /// <summary>Skip objects hidden behind others (GPU occlusion queries; see <see cref="OcclusionCuller"/>).</summary>
    public bool OcclusionCulling { get; set; } = true;

    /// <summary>Settings of the occlusion culling (which objects are worth testing).</summary>
    public OcclusionCuller Occlusion => _occlusion;

    /// <summary>Objects skipped this frame in the main view because they were outside it.</summary>
    public int ObjectsCulled { get; private set; }

    /// <summary>Objects skipped this frame in the main view because something hid them.</summary>
    public int ObjectsOccluded { get; private set; }

    /// <summary>Shadow casters skipped this frame because they were outside the sun's shadow box.</summary>
    public int ShadowCastersCulled { get; private set; }

    public Renderer(string shaderDirectory, int width, int height)
    {
        Lit = Load(shaderDirectory, "lit");
        Custom = new ShaderLibrary(shaderDirectory);
        Sky = Load(shaderDirectory, "sky");
        Depth = Load(shaderDirectory, "depth");
        PostShader = Load(shaderDirectory, "post");
        WaterShader = Load(shaderDirectory, "water");
        GrassShader = Load(shaderDirectory, "grass");
        _rippleMap = Texture.FromPixels(WaterNoise.NormalMap(512, 7), 512, 512, "water ripples", srgb: false);
        _rippleMap.SetMaxAnisotropy(16f);   // ripples stay sharp at grazing angles instead of blurring into mush
        Shadow = new ShadowMap(4096);
        Post = new PostProcess(shaderDirectory) { ExtraEffects = ScreenPasses };
        _occlusion = new OcclusionCuller();

        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        CreateSceneBuffers(requestedSamples: 4);
    }

    private static Shader Load(string directory, string name) =>
        new(Path.Combine(directory, name + ".vert"), Path.Combine(directory, name + ".frag"));

    public IEnumerable<Shader> Shaders
    {
        get
        {
            yield return Lit;
            yield return Sky;
            yield return Depth;
            yield return PostShader;
            yield return WaterShader;
            yield return GrassShader;
            foreach (var shader in Post.EffectShaders) yield return shader;
        }
    }

    /// <summary>Recompiles all shaders from disk. Shaders that fail to compile keep their previous version.</summary>
    public bool ReloadShaders()
    {
        bool ok = true;
        foreach (var shader in Shaders) ok &= shader.Reload();
        Custom.Invalidate();   // custom shaders are built from lit.frag, so they rebuild too
        return ok;
    }

    /// <summary>MSAA is used when the driver supports it; otherwise rendering falls back to no AA.</summary>
    private void CreateSceneBuffers(int requestedSamples)
    {
        _sceneBuffer?.Dispose();
        _resolveBuffer?.Dispose();
        _resolveBuffer = null;

        GL.GetInteger(GetPName.MaxSamples, out int maxSamples);
        int samples = Math.Min(requestedSamples, maxSamples);

        // Multisampled targets keep depth in a renderbuffer and resolve it into a depth texture for effects.
        _sceneBuffer = Framebuffer.CreateColor(_width, _height, samples, true,
            samples > 0 ? DepthMode.Renderbuffer : DepthMode.Texture);
        if (samples > 0 && !_sceneBuffer.IsComplete)
        {
            Log.Warn("Multisampled framebuffer unavailable; MSAA disabled.");
            samples = 0;
            _sceneBuffer.Dispose();
            _sceneBuffer = Framebuffer.CreateColor(_width, _height, 0, true, DepthMode.Texture);
        }

        MsaaSamples = samples;
        if (samples > 0)
        {
            _resolveBuffer = Framebuffer.CreateColor(_width, _height, 0, true, DepthMode.Texture);
            Log.Info($"MSAA {samples}x enabled.");
        }
    }

    /// <summary>Lower-left corner, in window pixels, where the finished image is drawn (the editor's viewport).</summary>
    public Vector2i OutputOffset { get; set; }

    public void Resize(int width, int height)
    {
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _sceneBuffer.Resize(_width, _height);
        _resolveBuffer?.Resize(_width, _height);
        _refractionBuffer?.Resize(_width, _height);
    }

    public void Render(Scene scene, Camera camera)
    {
        if (scene.Skybox?.Physical?.Clouds is { } skyClouds) skyClouds.QualityScale = CloudQuality;
        Custom.Update(AverageFrameTime);
        scene.CollectEnabled(_visible);
        Classify(camera);
        if (OcclusionCulling) _occlusion.BeginFrame();

        bool shadows = ShadowsEnabled && scene.Sun is { CastShadow: true, IsEmitting: true };
        if (shadows) RenderShadowPass(scene.Sun!);

        // Water is hidden from below (there is no underwater view).
        _activeWaters.Clear();
        foreach (var water in scene.Waters)
            if (water.Enabled && camera.Position.Y > water.Level + 0.02f &&
                (!FrustumCulling || water.ReachHorizon || new Frustum(camera.ViewMatrix * camera.ProjectionMatrix).Intersects(water.Bounds)))
                _activeWaters.Add(water);   // a lake out of view needs no reflection pass either
        foreach (var water in _activeWaters)
            if (water.ReflectionEnabled) RenderReflection(water, scene, camera, shadows);

        // Puddles on the ground mirror the world too (one extra pass, only while the ground is wet).
        _puddleReady = false;
        if (scene.Wetness > 0.05f && ReflectionsEnabled && Quality < 2)
        {
            _puddleBuffer ??= Framebuffer.CreateColor((int)(_width * PuddleScale * (Quality == 0 ? 1f : 0.7f)), (int)(_height * PuddleScale * (Quality == 0 ? 1f : 0.7f)), 0, true, DepthMode.Renderbuffer);
            _puddleBuffer.Resize((int)(_width * PuddleScale * (Quality == 0 ? 1f : 0.7f)), (int)(_height * PuddleScale * (Quality == 0 ? 1f : 0.7f)));
            RenderMirror(_puddleBuffer, scene.PuddleLevel, scene, camera, shadows);
            _puddleReady = true;
        }

        var clear = MathUtil.ToLinear(ClearColor);
        _sceneBuffer.Bind();
        GL.ClearColor(clear.X, clear.Y, clear.Z, 1f);
        GL.DepthMask(true);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Less);
        GL.Disable(EnableCap.Blend);
        SetPolygonMode(Wireframe);

        ObjectsDrawn = 0;
        DrawCalls = 0;

        Lit.Use();
        SetFrameUniforms(scene, camera.ViewMatrix, camera.ProjectionMatrix, camera.Position, shadows,
            new Vector4(0f, 0f, 0f, 1f));

        // Opaque objects front to back (the depth test then skips hidden pixels early), minus those the occlusion
        // queries found hidden; then new queries against the depth just drawn, answered on a later frame.
        ObjectsOccluded = 0;
        bool occlusion = OcclusionCulling && !Wireframe;
        foreach (var obj in _opaque)
        {
            if (occlusion && _occlusion.IsHidden(obj, camera.Position, camera.Near))
            {
                ObjectsOccluded++;
                continue;
            }
            DrawObject(obj, camera);
        }
        if (occlusion)
        {
            _occlusion.IssueQueries(_opaque, Depth, camera.ViewMatrix * camera.ProjectionMatrix, camera.Position, camera.Near);
            Lit.Use();
        }

        // Grass after the occlusion tests (blades must not hide objects from them), before the sky.
        if (scene.Grass is { Enabled: true } grass) DrawGrass(grass, scene, camera, shadows);

        // After opaque and before transparent objects, so blended surfaces show the sky behind them.
        if (SkyboxEnabled && scene.Skybox != null)
        {
            SetPolygonMode(false);
            var skyProgram = SkyProgram(scene, camera.Position);
            SetFogUniforms(skyProgram, scene);
            SetSeaUniforms(scene, camera, skyProgram);
            scene.Skybox.Draw(camera, skyProgram, scene.Sun);
            SetPolygonMode(Wireframe);
            Lit.Use();
        }

        // Water refracts what is already drawn, so it goes after opaque objects and sky.
        if (_activeWaters.Count > 0) DrawWaters(scene, camera);

        if (_transparent.Count > 0)
        {
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.DepthMask(false);
            foreach (var obj in _transparent) DrawObject(obj, camera);
            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
        }

        DrawHighlight(camera);
        DrawOverlay(camera);
        SetPolygonMode(false);

        // Resolve MSAA, then tone-map into the window's framebuffer.
        var resolved = _resolveBuffer ?? _sceneBuffer;
        if (_resolveBuffer != null) _sceneBuffer.BlitColorTo(_resolveBuffer);

        Post.Quality = Quality;
        Post.ShadowTexture = shadows ? Shadow.DepthTexture : 0;
        Post.ShadowMatrix = Shadow.LightSpaceMatrix;
        Post.Render(PostShader, resolved.ColorTexture, resolved.DepthTexture, camera, scene, _width, _height, OutputOffset);
    }

    // ---- water -----------------------------------------------------------------------------------

    /// <summary>
    /// Renders the opaque scene and the sky as seen in a mirror lying in the water plane: the view matrix is
    /// reflected (which flips triangle winding) and everything below the surface is clipped away.
    /// </summary>
    private void RenderReflection(Water water, Scene scene, Camera camera, bool shadows)
    {
        float scale = Math.Clamp(water.ReflectionScale, 0.25f, 1f);
        var target = water.GetReflectionBuffer((int)(_width * scale), (int)(_height * scale));
        RenderMirror(target, water.Level, scene, camera, shadows);
    }

    /// <summary>Renders the opaque scene and sky mirrored in the horizontal plane y = <paramref name="level"/> into <paramref name="target"/>.</summary>
    private void RenderMirror(Framebuffer target, float level, Scene scene, Camera camera, bool shadows)
    {
        var cloudLayer = scene.Skybox?.Physical?.Clouds;
        if (cloudLayer != null) cloudLayer.QualityScale = CloudQuality * 0.4f;
        _inMirror = true;
        var mirror = Matrix4.CreateTranslation(0f, -level, 0f) * Matrix4.CreateScale(1f, -1f, 1f) *
                     Matrix4.CreateTranslation(0f, level, 0f);
        var view = mirror * camera.ViewMatrix;
        var mirroredEye = new Vector3(camera.Position.X, 2f * level - camera.Position.Y, camera.Position.Z);

        var clear = MathUtil.ToLinear(ClearColor);
        target.Bind();
        GL.ClearColor(clear.X, clear.Y, clear.Z, 1f);
        GL.DepthMask(true);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Less);
        GL.Disable(EnableCap.Blend);
        SetPolygonMode(false);

        GL.FrontFace(FrontFaceDirection.Cw); // mirroring flips the winding
        GL.Enable(EnableCap.ClipDistance0);

        Lit.Use();
        SetFrameUniforms(scene, view, camera.ProjectionMatrix, mirroredEye, shadows,
            new Vector4(0f, 1f, 0f, -(level - 0.03f)));
        // The mirror sees its own view: cull against it (objects hidden in the main view may well show here).
        var mirrorFrustum = new Frustum(view * camera.ProjectionMatrix);
        foreach (var list in new[] { _opaque, _offscreenOpaque })
            foreach (var obj in list)
            {
                var bounds = obj.Bounds;
                if (bounds.Max.Y > level && (!FrustumCulling || mirrorFrustum.Intersects(bounds))) obj.Draw(camera, Lit);
            }

        GL.Disable(EnableCap.ClipDistance0);
        if (SkyboxEnabled && scene.Skybox != null)
        {
            var skyProgram = SkyProgram(scene, camera.Position);
            SetFogUniforms(skyProgram, scene);
            skyProgram.SetBool("uOcean", false);   // the mirrored sky is never seen below its horizon
            scene.Skybox.Draw(view, camera.ProjectionMatrix, camera.Position, skyProgram, scene.Sun);
        }
        GL.FrontFace(FrontFaceDirection.Ccw);
        _inMirror = false;
        if (cloudLayer != null) cloudLayer.QualityScale = CloudQuality;
    }

    /// <summary>Copies the scene so far (colour and depth) and draws every water surface over it.</summary>
    private void DrawWaters(Scene scene, Camera camera)
    {
        _refractionBuffer ??= Framebuffer.CreateColor(_width, _height, 0, true, DepthMode.Texture);
        _sceneBuffer.BlitColorTo(_refractionBuffer);
        _sceneBuffer.Bind(); // the blit leaves the default framebuffer bound

        GL.Enable(EnableCap.DepthTest);
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Back);
        SetPolygonMode(Wireframe);

        var shader = WaterShader;
        shader.Use();
        shader.SetMatrix4("uView", camera.ViewMatrix);
        shader.SetMatrix4("uProjection", camera.ProjectionMatrix);
        shader.SetVector3("uViewPos", camera.Position);
        shader.SetFloat("uNear", camera.Near);
        shader.SetFloat("uFar", camera.Far);

        var sun = scene.Sun;
        bool lit = sun is { IsEmitting: true };
        shader.SetVector3("uSunDir", lit ? sun!.DirectionToSun : Vector3.UnitY);
        shader.SetVector3("uSunColor", lit ? sun!.LightColor * scene.LightDim : Vector3.Zero);
        shader.SetVector3("uAmbient", MathUtil.ToLinear(scene.AmbientColor) * scene.AmbientIntensity * scene.AmbientDim);

        shader.SetInt("uReflection", 0);
        shader.SetInt("uSceneColor", 1);
        shader.SetInt("uSceneDepth", 2);
        shader.SetInt("uRippleMap", 3);
        _refractionBuffer.BindColorTexture(TextureUnit.Texture1);
        _refractionBuffer.BindDepthTexture(TextureUnit.Texture2);
        _rippleMap.Bind(TextureUnit.Texture3);

        var frustum = new Frustum(camera.ViewMatrix * camera.ProjectionMatrix);
        foreach (var water in _activeWaters)
        {
            water.UpdateOcean();   // first: it uploads textures, which must not disturb the bindings made below
            var ocean = water.Ocean!;

            bool reflect = water.ReflectionEnabled && water.ReflectionBuffer != null;
            shader.SetBool("uReflect", reflect);
            if (reflect) water.ReflectionBuffer!.BindColorTexture(TextureUnit.Texture0);

            shader.SetFloat("uTime", water.Time);
            GL.ActiveTexture(TextureUnit.Texture4);
            GL.BindTexture(TextureTarget.Texture2D, ocean.DisplacementTexture);
            GL.ActiveTexture(TextureUnit.Texture5);
            GL.BindTexture(TextureTarget.Texture2D, ocean.SlopeTexture);
            shader.SetInt("uOceanDisplacement", 4);
            shader.SetInt("uOceanSlope", 5);
            shader.SetFloat("uPatchSize", ocean.PatchSize);
            shader.SetFloat("uOceanTexel", ocean.PatchSize / OceanWaves.N);
            shader.SetFloat("uWaveHeight", MathF.Max(water.WaveHeight, 1e-4f));
            var splashes = water.Splashes;
            for (int i = 0; i < splashes.Length; i++) shader.SetVector4(SplashFields[i], splashes[i]);
            shader.SetFloat("uRain", scene.RainAmount);
            SetFogUniforms(shader, scene);

            shader.SetVector3("uShallowColor", MathUtil.ToLinear(water.ShallowColor));
            shader.SetVector3("uDeepColor", MathUtil.ToLinear(water.DeepColor));
            shader.SetVector3("uAbsorption", water.Absorption);
            shader.SetFloat("uDepthColorRate", water.DepthColorRate);
            shader.SetVector2("uWind", water.WindDirection);
            shader.SetFloat("uRippleStrength", water.RippleStrength);
            shader.SetFloat("uRippleScale", water.RippleScale);
            shader.SetFloat("uReflectionDistortion", water.ReflectionDistortion);
            shader.SetFloat("uRefractionStrength", water.RefractionStrength);
            shader.SetFloat("uFresnelF0", water.FresnelF0);
            shader.SetFloat("uSpecularStrength", water.SpecularStrength);
            shader.SetFloat("uShininess", MathF.Max(water.Shininess, 1f));
            shader.SetFloat("uSparkle", MathF.Max(water.Sparkle, 0f));
            shader.SetFloat("uCrestLight", MathF.Max(water.CrestLight, 0f));
            shader.SetFloat("uFoamAmount", water.FoamAmount);
            shader.SetFloat("uShoreFoam", water.ShoreFoamWidth);
            shader.SetFloat("uEdgeSoftness", water.EdgeSoftness);
            // Quadtree tiles: small near the camera, doubling in size with distance; off-screen ones are skipped. Each
            // tile morphs into the grid of the next larger one before that one takes over.
            water.SelectNodes(camera.Position, camera.Far, FrustumCulling ? frustum : null);
            var cover = water.Coverage;
            shader.SetVector4("uWaterRect", new Vector4((cover.X + cover.Z) * 0.5f, (cover.Y + cover.W) * 0.5f,
                (cover.Z - cover.X) * 0.5f, (cover.W - cover.Y) * 0.5f));
            shader.SetFloat("uEdgeFade", water.ReachHorizon ? MathF.Max(camera.Far * 0.25f, 1f) : MathF.Max(water.EdgeFade, 0f));
            shader.SetFloat("uGridCells", water.GridCells);
            foreach (var node in water.Nodes)
            {
                shader.SetMatrix4("uModel", water.NodeMatrix(node));
                shader.SetVector2("uCellSize", new Vector2(node.Size / water.GridCells));
                shader.SetVector2("uMorph", water.MorphRange(node));
                water.Grid.Draw();
                DrawCalls++;
            }
        }
        SetPolygonMode(Wireframe);
        Lit.Use(); // later passes (transparent objects, highlight) set uniforms on and draw with the lit shader
    }

    private readonly List<GameObject> _benders = new();

    /// <summary>Draws the grass with the same sun, shadows, ambient light and fog as everything else.</summary>
    private void DrawGrass(GrassLayer grass, Scene scene, Camera camera, bool shadows)
    {
        var shader = GrassShader;
        shader.Use();
        SetFrameUniforms(scene, camera.ViewMatrix, camera.ProjectionMatrix, camera.Position, shadows, new Vector4(0f, 0f, 0f, 1f), shader);
        shader.SetFloat("uGrassHeight", grass.Height);
        shader.SetFloat("uGrassWidth", grass.Width);
        var wind = grass.WindDirection.LengthSquared > 1e-6f ? grass.WindDirection.Normalized() : Vector2.UnitX;
        shader.SetVector2("uWind", wind * grass.WindStrength);
        shader.SetFloat("uWindSpeed", grass.WindSpeed);
        shader.SetFloat("uMaxDistance", grass.MaxDistance);
        shader.SetFloat("uFlowers", Math.Clamp(grass.Flowers, 0f, 1f));
        shader.SetVector3("uGrassBase", MathUtil.ToLinear(grass.BaseColor));
        shader.SetVector3("uGrassTip", MathUtil.ToLinear(grass.TipColor));

        // The player (and up to three more things tagged "Player") push the blades aside.
        _benders.Clear();
        if (grass.PlayerBendRadius > 0f)
            foreach (var obj in scene.Traverse())
                if (_benders.Count < 4 && obj.Enabled && string.Equals(obj.Tag, "Player", StringComparison.OrdinalIgnoreCase))
                    _benders.Add(obj);
        for (int i = 0; i < 4; i++)
        {
            var b = i < _benders.Count ? _benders[i] : null;
            float radius = b == null ? 0f : grass.PlayerBendRadius;
            var position = b == null ? Vector3.Zero : new Vector3(b.WorldPosition.X, b.Bounds.Min.Y, b.WorldPosition.Z);
            shader.SetVector4(BenderFields[i], new Vector4(position, radius));
        }

        GL.Disable(EnableCap.CullFace);   // blades are seen from both sides
        DrawCalls += grass.Draw(new Frustum(camera.ViewMatrix * camera.ProjectionMatrix), camera.Position);
        Lit.Use();
    }

    private static readonly string[] BenderFields = { "uBenders[0]", "uBenders[1]", "uBenders[2]", "uBenders[3]" };

    /// <summary>
    /// With a sea that reaches the horizon (and the camera above it), the sky draws distant sea below the horizon:
    /// the deep colour, turning into a mirror of the sky at grazing angles. It meets the water geometry where that fades
    /// out, so there is no grey band between the sea and the sky.
    /// </summary>
    private static void SetSeaUniforms(Scene scene, Camera camera, Shader sky)
    {
        var sea = scene.Waters.FirstOrDefault(w => w.Enabled && w.ReachHorizon && camera.Position.Y > w.Level);
        sky.SetBool("uOcean", sea != null);
        if (sea == null) return;
        var ambient = MathUtil.ToLinear(scene.AmbientColor) * scene.AmbientIntensity * scene.AmbientDim;
        var sunLight = scene.Sun is { IsEmitting: true } sun ? sun.LightColor * scene.LightDim * MathF.Max(sun.DirectionToSun.Y, 0f) * 0.35f : Vector3.Zero;
        sky.SetVector3("uOceanColor", MathUtil.ToLinear(sea.DeepColor) * (ambient + sunLight));
        sky.SetFloat("uOceanF0", sea.FresnelF0);
    }

    private static void SetPolygonMode(bool wireframe) =>
        GL.PolygonMode(TriangleFace.FrontAndBack, wireframe ? PolygonMode.Line : PolygonMode.Fill);

    private void RenderShadowPass(Sun sun)
    {
        Shadow.Begin(sun);
        GL.Disable(EnableCap.CullFace); // keeps thin geometry (planes) casting shadows
        SetPolygonMode(false);

        Depth.Use();
        Depth.SetMatrix4("uLightSpace", Shadow.LightSpaceMatrix);
        // Casters outside the sun's shadow box cannot cast into it: skip them (objects off screen still can).
        var lightFrustum = new Frustum(Shadow.LightSpaceMatrix);
        ShadowCastersCulled = 0;
        foreach (var obj in _visible)
        {
            if (!obj.CastShadow || !obj.Visible) continue;
            if (FrustumCulling && !lightFrustum.Intersects(obj.Bounds))
            {
                ShadowCastersCulled++;
                continue;
            }
            obj.DrawShadow(Depth);
        }
    }

    private void Classify(Camera camera)
    {
        _opaque.Clear();
        _transparent.Clear();
        _overlay.Clear();
        _offscreenOpaque.Clear();
        _sortBuffer.Clear();
        ObjectsCulled = 0;

        var frustum = new Frustum(camera.ViewMatrix * camera.ProjectionMatrix);
        var eye = camera.Position;
        foreach (var obj in _visible)
        {
            if (!obj.Visible) continue;
            if (obj.Overlay)
            {
                _overlay.Add(obj);
                continue;
            }

            var bounds = obj.Bounds;
            if (FrustumCulling && !frustum.Intersects(bounds))
            {
                ObjectsCulled++;
                // Off screen, but it may still show in a reflection: mirrors cull against their own view.
                if (!obj.IsTransparent) _offscreenOpaque.Add(obj);
                continue;
            }

            if (obj.IsTransparent) _transparent.Add(obj);
            else _sortBuffer.Add(((bounds.Center - eye).LengthSquared, obj));
        }

        // Opaque: near to far.
        _sortBuffer.Sort((a, b) => a.distance.CompareTo(b.distance));
        foreach (var (_, obj) in _sortBuffer) _opaque.Add(obj);

        // Transparent: back to front, farthest first.
        _transparent.Sort((a, b) =>
            (b.Bounds.Center - eye).LengthSquared.CompareTo((a.Bounds.Center - eye).LengthSquared));
    }

    private void DrawObject(GameObject obj, Camera camera)
    {
        // Objects with a custom shader (a .shader file) are drawn with their own program built from lit.frag
        var custom = Custom.Get(obj.Material.CustomShader);
        if (custom?.Program is { } program && _frameScene != null)
        {
            if (_frameCustom.Add(program))
                SetFrameUniforms(_frameScene, _frameView, _frameProjection, _frameViewPos, _frameShadows, _frameClip, program);
            program.Use();
            int textureUnit = FirstShaderTextureUnit;
            foreach (var param in custom.Params)
            {
                if (param.Kind == ShaderParamKind.Texture)
                {
                    if (textureUnit > LastShaderTextureUnit) continue;   // the units above belong to the engine
                    string path = obj.Material.ShaderTextures.TryGetValue(param.Name, out var own) ? own : param.DefaultTexture;
                    ShaderTexture(path).Bind(TextureUnit.Texture0 + textureUnit);
                    program.SetInt(param.Name, textureUnit++);
                    continue;
                }
                var value = obj.Material.ShaderParams.TryGetValue(param.Name, out var set) ? set : param.Default;
                switch (param.Kind)
                {
                    case ShaderParamKind.Float: program.SetFloat(param.Name, value.X); break;
                    case ShaderParamKind.Bool: program.SetBool(param.Name, value.X > 0.5f); break;
                    case ShaderParamKind.Vec2: program.SetVector2(param.Name, new Vector2(value.X, value.Y)); break;
                    case ShaderParamKind.Color: program.SetVector3(param.Name, MathUtil.ToLinear(new Color4(value.X, value.Y, value.Z, 1f))); break;
                    default: program.SetVector3(param.Name, new Vector3(value.X, value.Y, value.Z)); break;
                }
            }
            obj.Draw(camera, program, Wireframe);
            Lit.Use();
            ObjectsDrawn++;
            DrawCalls += obj.Model.Parts.Count;
            return;
        }

        obj.Draw(camera, Lit, Wireframe);
        ObjectsDrawn++;
        DrawCalls += obj.Model.Parts.Count;
    }

    // ---- screen shaders (the scene's Volume) -------------------------------------------------------

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScreenShaderUse, ScreenShaderPass> _screenPasses = new();
    private readonly List<PostEffect> _screenRun = new();

    /// <summary>The scene's enabled screen shaders that compiled, as steps of the post-processing chain.</summary>
    private IReadOnlyList<PostEffect> ScreenPasses(Scene scene)
    {
        _screenRun.Clear();
        foreach (var use in scene.Volume.ScreenShaders)
        {
            if (!use.Enabled || Custom.Get(use.Name) is not { IsScreen: true, Program: not null } shader) continue;
            var pass = _screenPasses.GetValue(use, _ => new ScreenShaderPass(ShaderTexture));
            pass.Source = shader;
            pass.Use = use;
            _screenRun.Add(pass);
        }
        return _screenRun;
    }

    /// <summary>
    /// The program that draws the scene's sky: its sky shader (a .shader file with a 'sky' block, given the sun and its
    /// settings) when it has one that compiled, otherwise the engine's own.
    /// </summary>
    private Shader SkyProgram(Scene scene, Vector3 cameraPosition)
    {
        if (scene.Skybox?.CustomShader is not { Enabled: true } use ||
            Custom.Get(use.Name) is not { IsSky: true, Program: { } program } custom) return Sky;

        var sun = scene.Sun;
        program.SetVector3("uUserSunDirection", sun?.DirectionToSun ?? Vector3.UnitY);
        program.SetVector3("uUserSunColor", sun is { IsEmitting: true } ? sun.LightColor * scene.LightDim : Vector3.Zero);
        program.SetFloat("uUserSunSize", (sun?.AngularRadius ?? 0.53f) * MathUtil.Deg2Rad);
        program.SetFloat("uUserTime", scene.WeatherTime);
        program.SetVector3("uUserCameraPosition", cameraPosition);

        int unit = FirstShaderTextureUnit;
        foreach (var param in custom.Params)
        {
            if (param.Kind == ShaderParamKind.Texture)
            {
                if (unit > LastShaderTextureUnit) continue;
                ShaderTexture(use.GetTexture(param)).Bind(TextureUnit.Texture0 + unit);
                program.SetInt(param.Name, unit++);
                continue;
            }
            var value = use.Get(param);
            switch (param.Kind)
            {
                case ShaderParamKind.Float: program.SetFloat(param.Name, value.X); break;
                case ShaderParamKind.Bool: program.SetBool(param.Name, value.X > 0.5f); break;
                case ShaderParamKind.Vec2: program.SetVector2(param.Name, new Vector2(value.X, value.Y)); break;
                case ShaderParamKind.Color: program.SetVector3(param.Name, MathUtil.ToLinear(new Color4(value.X, value.Y, value.Z, 1f))); break;
                default: program.SetVector3(param.Name, new Vector3(value.X, value.Y, value.Z)); break;
            }
        }
        return program;
    }

    /// <summary>The PS1-style vertex snapping the scene's screen shaders ask for (the finest grid wins; 0 = off).</summary>
    private float VertexSnap(Scene scene)
    {
        float snap = 0f;
        foreach (var use in scene.Volume.ScreenShaders)
            if (use.Enabled && Custom.Get(use.Name) is { IsScreen: true, VertexSnap: > 0f } shader)
                snap = snap == 0f ? shader.VertexSnap : MathF.Max(snap, shader.VertexSnap);
        return snap;
    }

    /// <summary>Texture units from here on hold the <c>texture</c> params of custom shaders (lower ones are the engine's).</summary>
    private const int FirstShaderTextureUnit = 8;

    /// <summary>The last unit custom shaders may use (14 holds the cloud weather, 15 is a scratch unit for uploads): 6 textures.</summary>
    private const int LastShaderTextureUnit = 13;

    private Texture? _white;
    private readonly HashSet<string> _missingTextures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The image of a shader's texture parameter (cached), or plain white if there is none or it cannot be read.</summary>
    private Texture ShaderTexture(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && !_missingTextures.Contains(path))
        {
            try
            {
                if (File.Exists(ModelCache.ResolvePath(path))) return ModelCache.GetTexture(path);
                Log.Warn($"Shader texture '{path}' not found; using white.");
            }
            catch (Exception e) when (e is IOException or InvalidOperationException)
            {
                Log.Warn($"Shader texture '{path}' could not be loaded: {e.Message}");
            }
            _missingTextures.Add(path);   // say it once, not every frame
        }
        return _white ??= ModelCache.Register("procedural:white", Texture.FromPixels(new byte[] { 255, 255, 255, 255 }, 1, 1, "white"));
    }

    /// <summary>Gizmos and other overlay objects: drawn last, ignoring depth so they are always visible.</summary>
    private void DrawOverlay(Camera camera)
    {
        if (_overlay.Count == 0) return;

        Lit.Use();
        Lit.SetVector4("uHighlight", Vector4.Zero);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.Blend);
        SetPolygonMode(false);
        foreach (var obj in _overlay) obj.Draw(camera, Lit);
        GL.Enable(EnableCap.DepthTest);
    }

    private void DrawHighlight(Camera camera)
    {
        var target = Highlight;
        if (target == null || target.IsDestroyed || !target.Enabled) return;

        Lit.SetVector4("uHighlight", new Vector4(HighlightColor.R, HighlightColor.G, HighlightColor.B, 1f));
        GL.Enable(EnableCap.PolygonOffsetLine);
        GL.PolygonOffset(-1f, -1f);
        GL.Disable(EnableCap.Blend);

        bool wasVisible = target.Visible;
        target.Visible = true;
        target.Draw(camera, Lit, forceWireframe: true);
        target.Visible = wasVisible;

        GL.Disable(EnableCap.PolygonOffsetLine);
        Lit.SetVector4("uHighlight", Vector4.Zero);
    }

    /// <summary>Uploads the world fog (shared by the lit, water and sky shaders).</summary>
    private static void SetFogUniforms(Shader shader, Scene scene)
    {
        // The volumetric fog effect does the real thing; without it the shaders blend a simple distance fog.
        shader.SetFloat("uFogDensity", scene.VolumetricFogActive ? 0f : scene.FogDensity);
        shader.SetFloat("uFogReflect", scene.FogDensity);
        shader.SetFloat("uStorm", scene.Storm);
        shader.SetVector3("uFogColor", scene.FogColor);
    }

    private void SetFrameUniforms(Scene scene, Matrix4 view, Matrix4 projection, Vector3 viewPos, bool shadows,
        Vector4 clipPlane, Shader? target = null)
    {
        var shader = target ?? Lit;
        if (target == null)
        {
            // Remember this pass so custom shaders used later in it get the same lights, fog and shadows
            _frameScene = scene;
            _frameView = view;
            _frameProjection = projection;
            _frameViewPos = viewPos;
            _frameShadows = shadows;
            _frameClip = clipPlane;
            _frameCustom.Clear();
        }
        shader.SetMatrix4("uView", view);
        shader.SetMatrix4("uProjection", projection);
        shader.SetVector3("uViewPos", viewPos);
        shader.SetVector4("uClipPlane", clipPlane);
        shader.SetVector3("uAmbient", MathUtil.ToLinear(scene.AmbientColor) * scene.AmbientIntensity * scene.AmbientDim);
        shader.SetVector4("uHighlight", Vector4.Zero);

        shader.SetInt("uDiffuseMap", 0);
        shader.SetInt("uSpecularMap", 1);
        shader.SetInt("uNormalMap", 2);
        shader.SetInt("uShadowMap", 3);
        shader.SetBool("uShadowsEnabled", shadows);
        if (shadows)
        {
            shader.SetMatrix4("uLightSpace", Shadow.LightSpaceMatrix);
            shader.SetFloat("uShadowTexel", Shadow.TexelSize);
            shader.SetFloat("uShadowBias", scene.Sun!.ShadowBias);
            Shadow.Bind(TextureUnit.Texture3);
        }

        // The mirror buffer is only sampled by the main pass, never while it is being drawn
        bool puddleMirror = _puddleReady && _puddleBuffer != null;
        shader.SetBool("uPuddleMirror", puddleMirror);
        if (puddleMirror)
        {
            shader.SetInt("uPuddleReflection", 7);
            _puddleBuffer!.BindColorTexture(TextureUnit.Texture7);
            shader.SetVector2("uScreenSize", new Vector2(_width, _height));
            shader.SetFloat("uPuddleLevel", scene.PuddleLevel);
        }
        SetFogUniforms(shader, scene);
        shader.SetFloat("uCloudStorm", scene.Storm);
        shader.SetFloat("uWetness", scene.Wetness);
        shader.SetFloat("uRainAmount", scene.RainAmount);
        shader.SetFloat("uWeatherTime", scene.WeatherTime);
        shader.SetVector2("uScreenSize", new Vector2(_width, _height));
        shader.SetFloat("uVertexSnap", VertexSnap(scene));

        // Caustics: what lies under the water gets the dancing light the waves focus onto it.
        int caustics = 0;
        foreach (var water in scene.Waters)
        {
            if (!water.Enabled || caustics == MaxCausticWaters) continue;
            var half = water.ReachHorizon ? new Vector2(1e7f) : water.Size * 0.5f;
            shader.SetVector4(CausticFields[caustics], new Vector4(water.Position.X, water.Position.Z, half.X, half.Y));
            shader.SetVector4($"uCausticInfo[{caustics}]", new Vector4(water.Level, water.Time, water.Caustics, water.WaveLength));
            caustics++;
        }
        shader.SetInt("uCausticCount", caustics);

        // The sampler must point at a 3D-texture unit even when cloud shadows are off.
        shader.SetInt("uCloudShape", 5);
        if (!_inMirror && scene.Skybox?.ActivePhysical?.Clouds is { } clouds) clouds.SetShadowUniforms(shader);   // too costly for the small mirror images
        else shader.SetBool("uCloudShadows", false);

        var sun = scene.Sun;
        bool hasSun = sun is { IsEmitting: true };
        shader.SetBool("uHasDirLight", hasSun);
        if (hasSun)
        {
            shader.SetVector3("uDirLight.direction", sun!.Direction);
            shader.SetVector3("uDirLight.color", sun.LightColor * scene.LightDim);
        }

        int count = 0;
        foreach (var light in scene.PointLights)
        {
            if (!light.Enabled || count >= Scene.MaxPointLights) continue;
            shader.SetVector3(PointLightFields[count * 3], light.Position);
            shader.SetVector3(PointLightFields[count * 3 + 1], MathUtil.ToLinear(light.Color) * light.Intensity);
            shader.SetFloat(PointLightFields[count * 3 + 2], light.Range);
            count++;
        }
        shader.SetInt("uPointLightCount", count);

        var spot = scene.Spot;
        bool hasSpot = spot is { Enabled: true };
        shader.SetBool("uHasSpot", hasSpot);
        if (hasSpot)
        {
            shader.SetVector3("uSpot.position", spot!.Position);
            shader.SetVector3("uSpot.direction", spot.Direction);
            shader.SetVector3("uSpot.color", MathUtil.ToLinear(spot.Color) * spot.Intensity);
            shader.SetFloat("uSpot.range", spot.Range);
            shader.SetFloat("uSpot.cosInner", MathF.Cos(spot.InnerAngle * MathUtil.Deg2Rad));
            shader.SetFloat("uSpot.cosOuter", MathF.Cos(spot.OuterAngle * MathUtil.Deg2Rad));
        }
    }

    private static string[] BuildPointLightFields()
    {
        var names = new string[Scene.MaxPointLights * 3];
        for (int i = 0; i < Scene.MaxPointLights; i++)
        {
            names[i * 3] = $"uPointLights[{i}].position";
            names[i * 3 + 1] = $"uPointLights[{i}].color";
            names[i * 3 + 2] = $"uPointLights[{i}].range";
        }
        return names;
    }

    public void Dispose()
    {
        _occlusion.Dispose();
        foreach (var shader in Shaders) shader.Dispose();
        Custom.Dispose();
        Post.Dispose();
        Shadow.Dispose();
        _sceneBuffer.Dispose();
        _resolveBuffer?.Dispose();
        _refractionBuffer?.Dispose();
        _rippleMap.Dispose();
    }
}
