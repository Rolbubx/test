using GT67.Utils;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>
/// The scene's sun. Unlike a plain light it has a place in the sky: its position comes from a
/// <see cref="TimeOfDay"/>, <see cref="Latitude"/>, <see cref="Declination"/> and <see cref="NorthRotation"/>
/// (or from <see cref="Direction"/> when you aim it yourself), and its light is tinted and dimmed by the
/// atmosphere when the skybox is a <see cref="PhysicalSky"/>. It also casts the scene's shadows.
/// </summary>
public sealed class Sun : Light
{
    private Vector3 _manualDirection = Vector3.Normalize(new Vector3(-0.4f, -1f, -0.3f));

    public Sun() => Name = "Sun";

    // ---- position --------------------------------------------------------------------------------

    /// <summary>Local solar time in hours (12 = noon). Wraps around at 24.</summary>
    public float TimeOfDay { get; set; } = 12f;

    /// <summary>In-game hours that pass per real second (0 = time stands still).</summary>
    public float TimeSpeed { get; set; }

    /// <summary>Observer latitude in degrees. Positive: the noon sun is to the south (+Z); negative: to the north (-Z).</summary>
    public float Latitude { get; set; } = 40f;

    /// <summary>Season, as the sun's declination in degrees: +23 mid-summer (north), 0 equinox, -23 mid-winter.</summary>
    public float Declination { get; set; } = 12f;

    /// <summary>Turns the compass around the Y axis, in degrees (shifts where the sun rises and sets).</summary>
    public float NorthRotation { get; set; }

    /// <summary>
    /// True: the sun follows the time of day. False: it points along <see cref="Direction"/>.
    /// Assigning <see cref="Direction"/> switches this off.
    /// </summary>
    public bool AutoPosition { get; set; } = true;

    /// <summary>Direction the light travels (from the sun towards the world).</summary>
    public Vector3 Direction
    {
        get => AutoPosition ? -DirectionFromTime() : _manualDirection;
        set
        {
            _manualDirection = value.LengthSquared > 0 ? Vector3.Normalize(value) : -Vector3.UnitY;
            AutoPosition = false;
        }
    }

    /// <summary>Unit vector from the world towards the sun.</summary>
    public Vector3 DirectionToSun => -Direction;

    /// <summary>Height above the horizon in degrees (negative = below).</summary>
    public float Elevation => MathF.Asin(MathUtil.Clamp(DirectionToSun.Y, -1f, 1f)) * MathUtil.Rad2Deg;

    /// <summary>Compass bearing in degrees: 0 = north (-Z), 90 = east (+X), 180 = south, 270 = west.</summary>
    public float Azimuth
    {
        get
        {
            var d = DirectionToSun;
            float degrees = MathF.Atan2(d.X, -d.Z) * MathUtil.Rad2Deg;
            return degrees < 0f ? degrees + 360f : degrees;
        }
    }

    public bool IsAboveHorizon => DirectionToSun.Y > 0f;

    // ---- appearance in the sky -------------------------------------------------------------------

    /// <summary>Angular radius of the visible disc in degrees (the real sun is about 0.27; enlarged for looks).</summary>
    public float AngularRadius { get; set; } = 0.53f;

    /// <summary>Brightness of the sun disc in the physical sky (feeds bloom and sun rays).</summary>
    public float DiscBrightness { get; set; } = 35f;

    /// <summary>False hides the disc in the sky but keeps the light (and shadows). <see cref="Light.Enabled"/> turns off both.</summary>
    public bool ShowDisc { get; set; } = true;

    // ---- lighting --------------------------------------------------------------------------------

    /// <summary>
    /// When the sky is a <see cref="PhysicalSky"/>: tint and dim the sun by the air its light crosses
    /// (orange and weak at sunset, off at night) and set the scene's ambient light from the sky.
    /// </summary>
    public bool AtmosphericLighting { get; set; } = true;

    /// <summary>Fraction of sunlight surviving the atmosphere, per channel. Set by the sky each frame.</summary>
    public Vector3 AtmosphericTint { get; internal set; } = Vector3.One;

    /// <summary>The colour the renderer lights with: colour x intensity x atmosphere, in linear space.</summary>
    public Vector3 LightColor => MathUtil.ToLinear(Color) * AtmosphericTint * Intensity;

    /// <summary>False when the sun is disabled or all of its light has been absorbed (night).</summary>
    public bool IsEmitting => Enabled && MathF.Max(LightColor.X, MathF.Max(LightColor.Y, LightColor.Z)) > 1e-4f;

    // ---- shadows ---------------------------------------------------------------------------------

    public bool CastShadow { get; set; } = true;

    /// <summary>Half-size of the orthographic shadow volume, in world units.</summary>
    public float ShadowSize { get; set; } = 30f;

    /// <summary>Centre of the shadow volume.</summary>
    public Vector3 ShadowCenter { get; set; } = Vector3.Zero;

    /// <summary>Depth bias (slope scaled in the shader).</summary>
    public float ShadowBias { get; set; } = 0.0025f;

    // ---- update ----------------------------------------------------------------------------------

    /// <summary>Advances time and refreshes the atmospheric lighting. Called by <see cref="Scene.Update"/>.</summary>
    public void Update(Scene scene, float deltaTime)
    {
        if (TimeSpeed != 0f)
        {
            TimeOfDay += TimeSpeed * deltaTime;
            TimeOfDay -= 24f * MathF.Floor(TimeOfDay / 24f);
        }

        if (AtmosphericLighting && scene.Skybox?.Physical is { Enabled: true } sky)
            sky.LightScene(scene, this);
        else
            AtmosphericTint = Vector3.One;
    }

    /// <summary>Unit vector towards the sun for the current time, latitude, season and compass rotation.</summary>
    private Vector3 DirectionFromTime()
    {
        float lat = Latitude * MathUtil.Deg2Rad;
        float dec = Declination * MathUtil.Deg2Rad;
        float hour = (TimeOfDay - 12f) / 12f * MathF.PI;

        float up = MathF.Sin(lat) * MathF.Sin(dec) + MathF.Cos(lat) * MathF.Cos(dec) * MathF.Cos(hour);
        float east = -MathF.Cos(dec) * MathF.Sin(hour);
        float north = MathF.Cos(lat) * MathF.Sin(dec) - MathF.Sin(lat) * MathF.Cos(dec) * MathF.Cos(hour);

        // World axes: +X east, +Y up, -Z north.
        var toSun = new Vector3(east, up, -north);
        if (NorthRotation != 0f)
            toSun = Vector3.Transform(toSun, Quaternion.FromAxisAngle(Vector3.UnitY, NorthRotation * MathUtil.Deg2Rad));
        return Vector3.Normalize(toSun);
    }
}
