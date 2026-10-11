// ToonSky: a stylised sky drawn entirely here: a day / sunset / night gradient that follows the sun, the sun as a hard
// disc with a soft glow ring, a moon opposite the sun and twinkling stars at night. Pick it in the Hierarchy >
// Environment > Sky > Shader. Blend mixes it with the engine's sky (0 = only the engine's).
// Colours here are linear light: 1 is white, the sun is much brighter.
param float Blend = 1.0 range 0 1
param color DayZenith = (0.16, 0.42, 0.95)
param color DayHorizon = (0.62, 0.84, 1.0)
param color SunsetHorizon = (1.0, 0.45, 0.2)
param color NightZenith = (0.01, 0.015, 0.05)
param color NightHorizon = (0.04, 0.06, 0.14)
param float SunSize = 3.0 range 0.5 12          // degrees, drawn bigger than the real sun for the cartoon look
param float SunGlow = 1.5 range 0 6
param bool Moon = true
param float Stars = 1.0 range 0 3

sky
{
    vec3 d = normalize(direction);
    vec3 s = normalize(sunDirection);
    float up = saturate(d.y);

    // How far the day has gone: 1 at noon, 0 at night; 'sunset' peaks while the sun is near the horizon
    float day = smoothstep(-0.15, 0.25, s.y);
    float sunset = (1.0 - smoothstep(0.0, 0.35, abs(s.y + 0.02))) * saturate(dot(normalize(d.xz + 1e-4), normalize(s.xz + 1e-4)) * 0.5 + 0.6);

    vec3 zenith = mix(NightZenith, DayZenith, day);
    vec3 horizon = mix(mix(NightHorizon, DayHorizon, day), SunsetHorizon, sunset);
    vec3 mine = mix(horizon, zenith, pow(up, 0.45)) * 0.7;
    if (d.y < 0.0) mine = horizon * 0.6;          // below the horizon: a darker band

    // The sun: a hard toon disc and a soft ring of light around it
    float size = SunSize * 0.0174533;
    float toSun = dot(d, s);
    vec3 sunTint = mix(vec3(1.0, 0.95, 0.85), SunsetHorizon * 1.4, sunset);
    mine += sunTint * sunDisc(d, s, size) * 12.0 * step(-0.05, s.y);
    mine += sunTint * pow(saturate(toSun), 220.0 / max(SunGlow, 0.01)) * SunGlow * day;

    // The moon, opposite the sun, with a darker bite out of it
    if (Moon)
    {
        vec3 m = -s;
        float moon = sunDisc(d, m, size * 0.8) * (1.0 - sunDisc(d, normalize(m + vec3(0.012, 0.01, 0.0)), size * 0.75));
        mine += vec3(0.9, 0.92, 1.0) * moon * 3.0 * (1.0 - day);
    }

    // Stars: one per cell of a fine grid on the sky, twinkling, only at night and well above the horizon
    vec3 cell = floor(d * 180.0);
    float star = step(0.9975, hash(cell)) * (0.6 + 0.4 * sin(time * 3.0 + hash(cell + 7.0) * 40.0));
    mine += vec3(star) * Stars * (1.0 - day) * smoothstep(0.05, 0.3, d.y);

    color = mix(color, mine, Blend);
}
