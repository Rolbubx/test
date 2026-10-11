// PS1: the look of the first PlayStation. A low resolution without smoothing, colours cut down to 15 bits with
// ordered dithering, vertices that jump between coarse screen positions (the famous wobble) and, if you want it,
// the short draw distance hidden in fog. Add it to a scene's Volume in the Hierarchy.
vertexSnap 160

param float Height = 240.0 range 120 720        // vertical resolution in pixels (the PS1 drew 240)
param float ColorDepth = 32.0 range 4 256       // levels per colour channel (the PS1 had 32)
param float Dithering = 1.0 range 0 1
param float FogAmount = 0.0 range 0 1           // 1 = the far distance disappears into FogColor
param float FogStart = 15.0 range 0 200
param float FogEnd = 45.0 range 1 400
param color FogColor = (0.22, 0.24, 0.3)

screen
{
    // Big pixels: every block of the screen takes the colour (and depth) of its centre
    vec2 pixels = vec2(floor(Height * resolution.x / resolution.y), Height);
    vec2 cell = floor(uv * pixels);
    vec2 centre = (cell + 0.5) / pixels;
    vec3 c = sampleScene(centre);

    float fog = smoothstep(FogStart, FogEnd, sampleDepth(centre)) * FogAmount;
    c = mix(c, FogColor, fog);

    // 15-bit colour: few levels per channel, with the 4x4 dither pattern hiding the bands
    float levels = max(ColorDepth - 1.0, 1.0);
    c += dither(cell) * Dithering / levels;
    color = floor(saturate(c) * levels + 0.5) / levels;
}
