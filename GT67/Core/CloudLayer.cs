using System.Diagnostics;
using GT67.Utils;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>
/// Volumetric clouds: a slab of 3D-noise density that the sky shader ray-marches, lit by the scene's
/// <see cref="Sun"/> (with self-shadowing, a silver lining and sky ambient) and drifting with the wind.
/// Assign it to <see cref="PhysicalSky.Clouds"/>. Distances are in metres (world units).
/// </summary>
public sealed class CloudLayer : IDisposable
{
    private Texture3D? _shape, _detail;
    private Texture? _weather;
    private int _builtSeed;
    private float _time;
    private Vector3 _offset;
    private bool _disposed;

    public bool Enabled { get; set; } = true;

    /// <summary>Clouds block the sun on the ground: moving cloud shadows on everything lit by the sun.</summary>
    /// <summary>0..1: pushes the layer towards a dark, overcast storm sky (set from the scene's weather; you can also set it yourself).</summary>
    public float Storminess { get; set; }
    /// <summary>Scales the ray-march step counts (the renderer lowers it for mirror images, which are small and soft).</summary>
    internal float QualityScale { get; set; } = 1f;

    private float Storm => Math.Clamp(Storminess, 0f, 1f);

    /// <summary>The cloud base sinks as the storm builds, so the ceiling closes in.</summary>
    private float EffectiveAltitude => Altitude * (1f - 0.3f * Storm);

    public bool CastShadows { get; set; } = true;

    /// <summary>How dark the cloud shadows get, 0 (none) to 1 (full).</summary>
    public float ShadowStrength { get; set; } = 0.85f;

    // ---- shape -----------------------------------------------------------------------------------

    /// <summary>How much of the sky is covered, 0 (clear) to 1 (overcast).</summary>
    public float Coverage { get; set; } = 0.5f;

    /// <summary>Thickness multiplier of the cloud material (higher = darker, more opaque clouds).</summary>
    public float Density { get; set; } = 1f;

    /// <summary>Height of the cloud base above the ground.</summary>
    public float Altitude { get; set; } = 1500f;

    /// <summary>Height of the cloud slab.</summary>
    public float Thickness { get; set; } = 2500f;

    /// <summary>Size in metres of one tile of the base noise (smaller = more, smaller clouds).</summary>
    public float ShapeSize { get; set; } = 9000f;

    /// <summary>How many times finer the erosion detail is than the base shape.</summary>
    public float DetailTiling { get; set; } = 6f;

    /// <summary>How strongly the detail noise erodes the cloud edges (0 = smooth blobs).</summary>
    public float DetailStrength { get; set; } = 0.55f;

    /// <summary>Changes the noise pattern and the weather (regenerates the textures).</summary>
    public int Seed { get; set; } = 1337;

    // ---- randomness ------------------------------------------------------------------------------

    /// <summary>
    /// How much the sky varies from place to place (0 = the same cover everywhere, 1 = clear patches next to thick,
    /// towering clouds): random weather from a noise field that never repeats.
    /// </summary>
    public float Variation { get; set; } = 0.6f;

    /// <summary>How fast the weather and the cloud shapes change by themselves (0 = they only drift with the wind).</summary>
    public float Evolution { get; set; } = 1f;

    /// <summary>Size in metres of the weather pattern (bigger = larger cloudy and clear regions).</summary>
    public float WeatherSize { get; set; } = 40000f;

    // ---- motion ----------------------------------------------------------------------------------

    /// <summary>Wind direction on the ground plane (x = east, y = south).</summary>
    public Vector2 WindDirection { get; set; } = new(1f, 0.25f);

    /// <summary>Wind speed in metres per second (0 freezes the clouds).</summary>
    public float WindSpeed { get; set; } = 12f;

    // ---- lighting --------------------------------------------------------------------------------

    /// <summary>Strength of the sun's light on the clouds.</summary>
    public float SunIntensity { get; set; } = 1.8f;

    /// <summary>Strength of the sky's ambient light on the clouds (their shaded sides).</summary>
    public float AmbientIntensity { get; set; } = 1.5f;

    /// <summary>Forward scattering of the cloud droplets: higher gives a brighter silver lining towards the sun.</summary>
    public float Anisotropy { get; set; } = 0.6f;

    /// <summary>Colour multiplier for the clouds (sRGB).</summary>
    public Color4 Tint { get; set; } = Color4.White;

    /// <summary>Fades distant clouds into the sky (per metre; 0 = no fade).</summary>
    public float Haze { get; set; } = 2e-5f;

    // ---- quality ---------------------------------------------------------------------------------

    /// <summary>Samples along each view ray. More = smoother, slower.</summary>
    public int Steps { get; set; } = 32;

    /// <summary>Samples along each ray towards the sun (cloud self-shadowing).</summary>
    public int LightSteps { get; set; } = 5;

    /// <summary>Clouds further away than this are not rendered.</summary>
    public float MaxDistance { get; set; } = 50000f;

    /// <summary>Moves the clouds with the wind. Called by <see cref="PhysicalSky.Update"/>.</summary>
    public void Update(float deltaTime)
    {
        var wind = WindDirection.LengthSquared > 1e-6f ? Vector2.Normalize(WindDirection) : Vector2.Zero;
        _offset += new Vector3(wind.X, 0f, wind.Y) * (WindSpeed * deltaTime);
        _time += deltaTime;
    }

    /// <summary>Uploads the cloud parameters and binds the noise textures used by sky.frag.</summary>
    internal void SetUniforms(Shader shader, Vector3 cameraPosition, Sun? sun, Vector3 skyAmbient)
    {
        if (!Enabled)
        {
            shader.SetBool("uClouds", false);
            return;
        }

        EnsureTextures();
        _shape!.Bind(TextureUnit.Texture5);
        _detail!.Bind(TextureUnit.Texture6);
        SetWeatherUniforms(shader);

        var tint = MathUtil.ToLinear(Tint);
        var sunLight = sun is { Enabled: true }
            ? MathUtil.ToLinear(sun.Color) * sun.AtmosphericTint * SunIntensity * (1f - 0.75f * Storm)
            : Vector3.Zero;

        shader.SetBool("uClouds", true);
        shader.SetInt("uCloudShape", 5);
        shader.SetInt("uCloudDetail", 6);
        shader.SetVector3("uCameraPos", cameraPosition);
        shader.SetFloat("uCloudMin", EffectiveAltitude);
        shader.SetFloat("uCloudStorm", Storm);
        shader.SetFloat("uCloudMax", EffectiveAltitude + MathF.Max(Thickness * (1f + 0.2f * Storm), 1f));
        shader.SetFloat("uCloudCoverage", Math.Clamp(Coverage + (1.35f - Coverage) * Storm, 0f, 1.5f));
        shader.SetFloat("uCloudDensity", Density * (1f + 2f * Storm));   // storm clouds are thicker and darker
        shader.SetFloat("uCloudShapeScale", 1f / MathF.Max(ShapeSize, 1f));
        shader.SetFloat("uCloudDetailTiling", DetailTiling);
        shader.SetFloat("uCloudDetailStrength", Math.Clamp(DetailStrength, 0f, 0.95f));
        shader.SetVector3("uCloudOffset", _offset);
        shader.SetInt("uCloudSteps", Math.Clamp((int)(Steps * QualityScale), 4, 128));
        shader.SetInt("uCloudLightSteps", Math.Clamp((int)(LightSteps * QualityScale + 0.5f), 1, 12));
        shader.SetVector3("uCloudSunColor", sunLight * tint);
        shader.SetVector3("uCloudAmbient", skyAmbient * AmbientIntensity * (1f - 0.7f * Storm) * tint);
        shader.SetFloat("uCloudAnisotropy", Math.Clamp(Anisotropy, 0f, 0.95f));
        shader.SetFloat("uCloudMaxDistance", MaxDistance);
        shader.SetFloat("uCloudHaze", Haze);
    }

    /// <summary>Uploads the cloud shadow parameters used by lit.frag (the shape texture goes on unit 5, as for the sky).</summary>
    internal void SetShadowUniforms(Shader shader) => SetFieldUniforms(shader, CastShadows && ShadowStrength > 0f);

    /// <summary>Same as <see cref="SetShadowUniforms"/> but for the god-ray mask (always on while the clouds are).</summary>
    internal void SetOcclusionUniforms(Shader shader) => SetFieldUniforms(shader, true);

    private void SetFieldUniforms(Shader shader, bool wanted)
    {
        bool on = Enabled && wanted;
        shader.SetBool("uCloudShadows", on);
        if (!on) return;

        EnsureTextures();
        _shape!.Bind(TextureUnit.Texture5);
        SetWeatherUniforms(shader);
        shader.SetFloat("uCloudMin", EffectiveAltitude);
        shader.SetFloat("uCloudStorm", Storm);
        shader.SetFloat("uCloudMax", EffectiveAltitude + MathF.Max(Thickness * (1f + 0.2f * Storm), 1f));
        shader.SetFloat("uCloudCoverage", Math.Clamp(Coverage + (1.35f - Coverage) * Storm, 0f, 1.5f));
        shader.SetFloat("uCloudDensity", Density * (1f + 2f * Storm));   // storm clouds are thicker and darker
        shader.SetFloat("uCloudShapeScale", 1f / MathF.Max(ShapeSize, 1f));
        shader.SetVector3("uCloudOffset", _offset);
        shader.SetFloat("uCloudShadowStrength", Math.Clamp(ShadowStrength + (1f - ShadowStrength) * Storm, 0f, 1f));
    }

    private void EnsureTextures()
    {
        if (_shape != null && _detail != null && _weather != null && _builtSeed == Seed) return;

        _shape?.Dispose();
        _detail?.Dispose();
        _weather?.Dispose();
        var watch = Stopwatch.StartNew();
        _shape = Texture3D.FromRgba(CloudNoise.Shape(64, Seed), 64);
        _detail = Texture3D.FromRgba(CloudNoise.Detail(32, Seed + 100), 32);
        // Random weather: periodic, domain-warped Perlin noise (alpha channel), sampled at kilometre scale.
        _weather = Texture.FromPixels(WaterNoise.NormalMap(128, Seed + 200), 128, 128, "cloud weather", srgb: false);
        _builtSeed = Seed;
        Log.Info($"Cloud noise generated in {watch.ElapsedMilliseconds} ms.");
    }

    /// <summary>The texture unit of the weather map (one no other pass samples from).</summary>
    private const int WeatherUnit = 14;

    /// <summary>The random weather: where clouds gather and how tall they grow, slowly changing over time.</summary>
    private void SetWeatherUniforms(Shader shader)
    {
        _weather!.Bind(TextureUnit.Texture0 + WeatherUnit);
        shader.SetInt("uCloudWeather", WeatherUnit);
        shader.SetFloat("uCloudTime", _time);
        shader.SetFloat("uCloudEvolution", MathF.Max(Evolution, 0f));
        shader.SetFloat("uCloudWeatherScale", 1f / MathF.Max(WeatherSize, 100f));
        shader.SetFloat("uCloudVariation", Math.Clamp(Variation, 0f, 1f));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shape?.Dispose();
        _detail?.Dispose();
        _weather?.Dispose();
    }
}
