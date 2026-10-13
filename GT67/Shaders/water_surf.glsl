// ---- surf: waves rolling onto a beach (shared by water.vert and water.frag through #include) ----------------------
// Everything follows the depth of the water (d, metres down to the bed, negative under the sand), so the crests run
// along the depth lines, parallel to any shore. A cycle per wave: the crest comes in from uSurfDepth, rises as the
// water shoals (its front steepens), breaks into white water when the depth is about 1.4 times its height, and
// reaches the beach; then the swash runs up the sand (the water level near the shore rises) and slides back.
// The cycle is shifted along the shore by a slow noise, so the crest lines are curved and break unevenly.

uniform float uSurfHeight;    // height of the waves (0 = no surf)
uniform float uSurfPeriod;    // seconds between waves
uniform float uSurfDepth;     // depth (m) where the waves start to rise
uniform float uSwashHeight;   // how high (m) the water runs up the beach after each wave

const float SURF_ARRIVE = 0.62;   // share of the cycle the crest takes to come in; the rest is the swash

struct Surf
{
    float lift;    // how much the surface is raised here (m)
    float wave;    // 0..1, the crest's height here (for the light through it)
    float foam;    // white water: the breaking lip and what it leaves behind
    float swash;   // the swash level now (m above the water level), for its foam
};

// Where this spot is in the wave cycle, 0..1: x for the crest, y for the swash. The swash only follows a very slow
// noise: across the few metres it runs up the sand it must rise and fall together (or it leaves stray pockets of
// water on the beach); the crest adds a finer noise, so its line is wavy and breaks unevenly.
vec2 surfCycle(vec2 xz, sampler2D noise, float time)
{
    float slow = textureLod(noise, xz * 0.0055, 0.0).a;
    float fine = textureLod(noise, xz * 0.03 + 0.37, 0.0).a;
    float t = time / max(uSurfPeriod, 0.5) + slow * 0.8;
    return vec2(fract(t + (fine - 0.5) * 0.18), fract(t));
}

Surf surfAt(vec2 cycle, float d)
{
    Surf s = Surf(0.0, 0.0, 0.0, 0.0);
    float D = max(uSurfDepth, 0.1);
    float u = cycle.x;

    // The swash: right after the wave reaches the beach the water runs up, then slides slowly back
    float w = cycle.y;
    s.swash = uSwashHeight * smoothstep(SURF_ARRIVE - 0.05, SURF_ARRIVE + 0.07, w) * (1.0 - smoothstep(SURF_ARRIVE + 0.1, 1.0, w));
    s.lift = s.swash * (1.0 - smoothstep(0.0, D * 0.6, d));

    if (uSurfHeight <= 0.0 || d < -0.05 || d > D * 1.3) return s;

    float a = clamp(u / SURF_ARRIVE, 0.0, 1.0);
    float crest = D * pow(1.0 - a, 1.4);                         // the crest slows down as the water gets shallow
    float grow = smoothstep(D, D * 0.45, crest);                 // shoaling: it rises as it comes in
    float breakDepth = min(uSurfHeight * 1.4, D * 0.7);
    float broken = smoothstep(breakDepth * 1.25, breakDepth * 0.75, crest);
    float fade = 1.0 - smoothstep(0.85, 1.0, a);
    float amp = uSurfHeight * grow * mix(1.0, 0.4, broken) * mix(0.3, 1.0, smoothstep(0.0, breakDepth, crest)) * fade;

    // Steep front (towards the beach, x < 0), long gentle back
    float x = d - crest;
    float front = mix(0.12, 0.035, grow) * D, back = 0.22 * D;
    float h = x < 0.0 ? exp(-x * x / (front * front)) : exp(-x * x / (back * back));
    s.wave = h * grow * fade;
    s.lift += amp * h;

    // White water: spilling from the top of the lip as it breaks, then a band of foam it leaves behind
    float lip = x < 0.0 ? exp(-x * x / (front * front * 0.6)) : exp(-x / (0.18 * D));
    float spill = smoothstep(breakDepth * 1.5, breakDepth, crest) * pow(h, 6.0);
    s.foam = (broken * lip + spill) * fade;
    return s;
}
