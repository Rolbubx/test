using GT67.Utils;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>
/// Physically based atmosphere: single scattering with Rayleigh (air) and Mie (haze) particles, ray-marched in the
/// sky shader. Lengths are in kilometres. It describes the air only; the sun's position, disc and light belong to
/// <see cref="Scene.Sun"/>, which this sky tints and dims (see <see cref="Core.Sun.AtmosphericLighting"/>).
/// </summary>
public sealed class PhysicalSky : IDisposable
{
    private const int ViewSteps = 16;
    private const int LightSteps = 8;
    private Vector3 _ambientLight = new(0.2f, 0.25f, 0.4f);

    /// <summary>
    /// False switches the atmosphere off (with its clouds, the sun's tint and the automatic ambient light): the sky is
    /// then the skybox's gradient (<see cref="Skybox.TopColor"/>...) or a sky shader, and the ambient light is set by hand.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public float PlanetRadius { get; set; } = 6360f;
    public float AtmosphereHeight { get; set; } = 60f;

    /// <summary>Rayleigh scattering per km at sea level for red/green/blue (this is what makes the sky blue).</summary>
    public Vector3 RayleighCoefficient { get; set; } = new(5.8e-3f, 13.5e-3f, 33.1e-3f);

    /// <summary>Mie scattering per km at sea level (haze/dust; higher = hazier, whiter sky around the sun).</summary>
    public float MieCoefficient { get; set; } = 21e-3f;

    public float RayleighScaleHeight { get; set; } = 7.994f;
    public float MieScaleHeight { get; set; } = 1.2f;

    /// <summary>Mie phase anisotropy: how strongly haze scatters towards the sun (0 = evenly).</summary>
    public float MieAnisotropy { get; set; } = 0.76f;

    /// <summary>Brightness of the scattered light.</summary>
    public float Brightness { get; set; } = 20f;

    public Color4 GroundAlbedo { get; set; } = new(0.3f, 0.3f, 0.32f, 1f);
    public bool Stars { get; set; } = true;

    /// <summary>How much of the sky's average colour becomes the scene's ambient light.</summary>
    public float AmbientScale { get; set; } = 0.6f;

    /// <summary>Faint moon/starlight ambient added when the sky is dark.</summary>
    public Color4 NightAmbient { get; set; } = new(0.35f, 0.45f, 0.8f, 1f);

    public float NightAmbientIntensity { get; set; } = 0.04f;

    /// <summary>Volumetric clouds drifting in this sky (null = clear sky).</summary>
    public CloudLayer? Clouds { get; set; }

    // ---- scene integration -----------------------------------------------------------------------

    /// <summary>Animates the clouds. Called by <see cref="Scene.Update"/>.</summary>
    public void Update(float deltaTime) => Clouds?.Update(deltaTime);

    // The sun disc fades out as the storm builds (the cloud sheet hides the rest)
    private float DiscVisibility { get { float s = Math.Clamp(Clouds?.Storminess ?? 0f, 0f, 1f); float x = Math.Clamp(s / 0.6f, 0f, 1f); return 1f - x * x * (3f - 2f * x); } }

    public void Dispose() => Clouds?.Dispose();

    /// <summary>Tints/dims <paramref name="sun"/> by the atmosphere and sets the scene's ambient light from the sky.</summary>
    internal void LightScene(Scene scene, Sun sun)
    {
        var toSun = sun.DirectionToSun;
        sun.AtmosphericTint = SunTransmittance(toSun);

        // Ambient: average sky radiance towards the zenith and four directions at 45 degrees elevation.
        var sky = Radiance(Vector3.UnitY, toSun);
        for (int i = 0; i < 4; i++)
        {
            float azimuth = i * MathF.PI * 0.5f;
            sky += Radiance(Vector3.Normalize(new Vector3(MathF.Cos(azimuth), 1f, MathF.Sin(azimuth))), toSun);
        }
        sky = sky / 5f * AmbientScale;
        sky += MathUtil.ToLinear(NightAmbient) * NightAmbientIntensity;
        _ambientLight = sky;

        float max = MathF.Max(sky.X, MathF.Max(sky.Y, MathF.Max(sky.Z, 1e-5f)));
        scene.AmbientColor = MathUtil.ToSrgb(sky / max);
        scene.AmbientIntensity = max;
    }

    // ---- GPU -------------------------------------------------------------------------------------

    /// <summary>Uploads the atmosphere and sun-disc parameters used by sky.frag.</summary>
    internal void SetUniforms(Shader shader, Vector3 cameraPosition, Sun? sun)
    {
        shader.SetBool("uAtmosphere", true);
        shader.SetVector3("uSunDir", sun?.DirectionToSun ?? Vector3.UnitY);
        shader.SetFloat("uSunIntensity", Brightness);
        shader.SetFloat("uSunDiscBrightness", sun is { Enabled: true, ShowDisc: true } ? sun.DiscBrightness * DiscVisibility : 0f);
        shader.SetFloat("uSunRadius", (sun?.AngularRadius ?? 0.53f) * MathUtil.Deg2Rad);
        shader.SetFloat("uCameraHeight", MathF.Max(cameraPosition.Y * 0.001f, 0.002f)); // world units are metres
        shader.SetFloat("uPlanetRadius", PlanetRadius);
        shader.SetFloat("uAtmosphereRadius", PlanetRadius + AtmosphereHeight);
        shader.SetVector3("uBetaR", RayleighCoefficient);
        shader.SetFloat("uBetaM", MieCoefficient);
        shader.SetFloat("uHr", RayleighScaleHeight);
        shader.SetFloat("uHm", MieScaleHeight);
        shader.SetFloat("uMieG", MieAnisotropy);
        shader.SetVector3("uGroundAlbedo", MathUtil.ToLinear(GroundAlbedo));
        shader.SetBool("uStars", Stars);

        if (Clouds != null) Clouds.SetUniforms(shader, cameraPosition, sun, _ambientLight);
        else shader.SetBool("uClouds", false);
    }

    // ---- CPU evaluation (same model as the shader; used for lighting) ------------------------------

    /// <summary>Fraction of sunlight reaching the ground per colour channel (zero when the sun is below the horizon).</summary>
    public Vector3 SunTransmittance(Vector3 toSun)
    {
        float radius = PlanetRadius;
        var origin = new Vector3(0f, radius + 0.002f, 0f);
        if (RaySphere(origin, toSun, radius, out float p0, out _) && p0 > 0f) return Vector3.Zero;
        if (!RaySphere(origin, toSun, radius + AtmosphereHeight, out _, out float top)) return Vector3.One;

        const int steps = 64;
        float ds = top / steps;
        float odR = 0f, odM = 0f;
        for (int i = 0; i < steps; i++)
        {
            float h = (origin + toSun * (ds * (i + 0.5f))).Length - radius;
            odR += MathF.Exp(-h / RayleighScaleHeight) * ds;
            odM += MathF.Exp(-h / MieScaleHeight) * ds;
        }
        return Exp(-(RayleighCoefficient * odR + new Vector3(MieCoefficient * 1.1f * odM)));
    }

    /// <summary>Scattered sky radiance in direction <paramref name="dir"/> (no sun disc), linear HDR.</summary>
    public Vector3 Radiance(Vector3 dir, Vector3 toSun)
    {
        float radius = PlanetRadius, top = radius + AtmosphereHeight;
        var origin = new Vector3(0f, radius + 0.002f, 0f);
        if (!RaySphere(origin, dir, top, out float t0, out float t1)) return Vector3.Zero;
        t0 = MathF.Max(t0, 0f);
        float tMax = RaySphere(origin, dir, radius, out float p0, out _) && p0 > 0f ? p0 : t1;

        float mu = Vector3.Dot(dir, toSun);
        float g = MieAnisotropy;
        float phaseR = 3f / (16f * MathF.PI) * (1f + mu * mu);
        float phaseM = 3f / (8f * MathF.PI) * ((1f - g * g) * (1f + mu * mu)) /
                       ((2f + g * g) * MathF.Pow(1f + g * g - 2f * g * mu, 1.5f));

        float ds = (tMax - t0) / ViewSteps;
        float odR = 0f, odM = 0f;
        var sumR = Vector3.Zero;
        var sumM = Vector3.Zero;

        for (int i = 0; i < ViewSteps; i++)
        {
            var pos = origin + dir * (t0 + ds * (i + 0.5f));
            float h = pos.Length - radius;
            float hr = MathF.Exp(-h / RayleighScaleHeight) * ds;
            float hm = MathF.Exp(-h / MieScaleHeight) * ds;
            odR += hr;
            odM += hm;

            if (RaySphere(pos, toSun, radius, out float s0, out _) && s0 > 0f) continue; // in the planet's shadow
            RaySphere(pos, toSun, top, out _, out float l1);

            float lds = l1 / LightSteps;
            float lodR = 0f, lodM = 0f;
            for (int j = 0; j < LightSteps; j++)
            {
                float lh = (pos + toSun * (lds * (j + 0.5f))).Length - radius;
                lodR += MathF.Exp(-lh / RayleighScaleHeight) * lds;
                lodM += MathF.Exp(-lh / MieScaleHeight) * lds;
            }

            var attenuation = Exp(-(RayleighCoefficient * (odR + lodR) + new Vector3(MieCoefficient * 1.1f * (odM + lodM))));
            sumR += attenuation * hr;
            sumM += attenuation * hm;
        }

        return (sumR * RayleighCoefficient * phaseR + sumM * MieCoefficient * phaseM) * Brightness;
    }

    private static Vector3 Exp(Vector3 v) => new(MathF.Exp(v.X), MathF.Exp(v.Y), MathF.Exp(v.Z));

    private static bool RaySphere(Vector3 origin, Vector3 dir, float radius, out float t0, out float t1)
    {
        float b = Vector3.Dot(origin, dir);
        float c = Vector3.Dot(origin, origin) - radius * radius;
        float discriminant = b * b - c;
        if (discriminant < 0f)
        {
            t0 = t1 = 0f;
            return false;
        }
        float s = MathF.Sqrt(discriminant);
        t0 = -b - s;
        t1 = -b + s;
        return true;
    }
}
