using GT67.Utils;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>
/// Cube drawn around the camera. Renders a physically based atmosphere, a cubemap, or a procedural gradient.
/// Drawn after opaque geometry with depth func LEQUAL and the view translation removed.
/// </summary>
public sealed class Skybox : IDisposable
{
    private readonly Mesh _cube = Primitives.Cube(2f);
    private bool _disposed;

    public Cubemap? Cubemap { get; }

    /// <summary>When set, the sky is a ray-marched atmosphere lit by the scene's <see cref="Scene.Sun"/>.</summary>
    public PhysicalSky? Physical { get; }

    /// <summary>Gradient colours (sRGB) used when there is no cubemap.</summary>
    public Color4 TopColor { get; set; } = new(0.22f, 0.42f, 0.78f, 1f);
    public Color4 HorizonColor { get; set; } = new(0.74f, 0.82f, 0.92f, 1f);
    public Color4 BottomColor { get; set; } = new(0.30f, 0.30f, 0.33f, 1f);

    /// <summary>
    /// A sky shader (a .shader file with a <c>sky</c> block) run over this sky, with its settings; null = none.
    /// <c>scene.Skybox.CustomShader = new ScreenShaderUse("ToonSky");</c>
    /// </summary>
    public ScreenShaderUse? CustomShader { get; set; }

    /// <summary>The physical atmosphere, when there is one and it is switched on.</summary>
    public PhysicalSky? ActivePhysical => Physical is { Enabled: true } physical ? physical : null;

    public Skybox(Cubemap? cubemap = null) => Cubemap = cubemap;

    public Skybox(PhysicalSky physical) => Physical = physical;

    /// <param name="sun">The scene's sun (position and disc for the physical sky); may be null.</param>
    public void Draw(Camera camera, Shader shader, Sun? sun) =>
        Draw(camera.ViewMatrix, camera.ProjectionMatrix, camera.Position, shader, sun);

    /// <summary>
    /// Draws with explicit matrices (the water's mirrored view uses a reflected view matrix).
    /// <paramref name="cameraPosition"/> is where the clouds are viewed from.
    /// </summary>
    public void Draw(Matrix4 viewMatrix, Matrix4 projection, Vector3 cameraPosition, Shader shader, Sun? sun)
    {
        var view = new Matrix4(new Matrix3(viewMatrix)); // drops the translation

        shader.Use();
        shader.SetMatrix4("uView", view);
        shader.SetMatrix4("uProjection", projection);
        shader.SetBool("uHasCubemap", Cubemap != null);
        shader.SetBool("uAtmosphere", false);
        if (Physical is { Enabled: true }) Physical.SetUniforms(shader, cameraPosition, sun);
        else
        {
            // The gradient sky still shows the sun's disc
            shader.SetBool("uClouds", false);
            shader.SetVector3("uSunDir", sun?.DirectionToSun ?? Vector3.UnitY);
            shader.SetFloat("uSunDiscBrightness", sun is { Enabled: true, ShowDisc: true } ? sun.DiscBrightness : 0f);
            shader.SetFloat("uSunRadius", (sun?.AngularRadius ?? 0.53f) * MathUtil.Deg2Rad);
        }
        shader.SetVector3("uTop", MathUtil.ToLinear(TopColor));
        shader.SetVector3("uHorizon", MathUtil.ToLinear(HorizonColor));
        shader.SetVector3("uBottom", MathUtil.ToLinear(BottomColor));
        shader.SetInt("uCubemap", 4);
        Cubemap?.Bind(TextureUnit.Texture4);

        GL.DepthFunc(DepthFunction.Lequal);
        GL.DepthMask(false);
        GL.Disable(EnableCap.CullFace);
        _cube.Draw();
        GL.DepthMask(true);
        GL.DepthFunc(DepthFunction.Less);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cube.Dispose();
        Physical?.Dispose();
        Cubemap?.Dispose();
    }
}
