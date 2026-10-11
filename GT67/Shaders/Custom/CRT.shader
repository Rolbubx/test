// CRT: an old television. Curved glass, scanlines, the red/green/blue stripes of the screen's mask, a little colour
// bleeding and darker corners. Add it to a scene's Volume in the Hierarchy (after PS1 for the full retro look).
param float Curvature = 0.08 range 0 0.3
param float Scanlines = 0.35 range 0 1
param float LineCount = 240.0 range 100 1000    // how many scanlines from top to bottom
param float Mask = 0.25 range 0 1
param float Bleed = 1.5 range 0 4               // colour fringes, in pixels
param float Vignette = 0.4 range 0 1
param float Flicker = 0.02 range 0 0.1

screen
{
    // Curved glass: push the picture outwards towards the corners; outside it is the dark bezel
    vec2 p = uv * 2.0 - 1.0;
    p += p * dot(p, p) * Curvature;
    vec2 q = p * 0.5 + 0.5;
    if (q.x < 0.0 || q.x > 1.0 || q.y < 0.0 || q.y > 1.0)
    {
        color = vec3(0.0);
        return;
    }

    float px = Bleed / resolution.x;
    vec3 c = vec3(sampleScene(q + vec2(px, 0.0)).r, sampleScene(q).g, sampleScene(q - vec2(px, 0.0)).b);

    float line = 0.5 + 0.5 * cos(q.y * LineCount * 6.2831853);
    c *= 1.0 - Scanlines * (1.0 - line);

    float stripe = mod(floor(q.x * resolution.x), 3.0);
    vec3 mask = stripe < 1.0 ? vec3(1.0, 1.0 - Mask, 1.0 - Mask) : stripe < 2.0 ? vec3(1.0 - Mask, 1.0, 1.0 - Mask) : vec3(1.0 - Mask, 1.0 - Mask, 1.0);
    c *= mask * (1.0 + Mask * 0.7);   // the mask darkens: make up for it

    c *= 1.0 - Vignette * dot(p, p) * 0.45;
    c *= 1.0 - Flicker * (0.5 + 0.5 * sin(time * 110.0));
    color = c;
}
