// Hologram: a see-through glowing projection with scanlines and a bright rim.
param color Tint = (0.2, 0.8, 1.0)
param float Lines = 120.0 range 10 400
param float RimPower = 2.5 range 0.5 8
param bool Flicker = true
param texture Pattern = ""                     // optional image laid over it (white when empty)

surface
{
    float scan = stripes(screenUV.y + time * 0.05, Lines);
    float rim = fresnel(normal, view, RimPower);
    float flicker = Flicker ? 0.85 + 0.15 * step(0.5, noise(vec2(time * 8.0, 0.0))) : 1.0;
    vec3 pattern = texture(Pattern, uv).rgb;

    albedo = vec3(0.0);
    specular = 0.0;
    emission = Tint * pattern * (0.25 + 0.75 * scan + rim * 2.0) * flicker;
    alpha = (scan > 0.25 || rim > 0.3) ? 1.0 : 0.0;   // gaps between the lines are cut away
}
