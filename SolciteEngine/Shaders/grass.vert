layout(location = 0) in vec3 aBlade;      // x -0.5..0.5 across the blade, y 0 (root) .. 1 (tip)
layout(location = 5) in vec4 aInstance;   // root position (world), seed 0..1

uniform mat4 uView;
uniform mat4 uProjection;
uniform vec3 uViewPos;
uniform float uWeatherTime;

uniform float uGrassHeight;
uniform float uGrassWidth;
uniform vec2 uWind;            // direction * strength
uniform float uWindSpeed;
uniform float uMaxDistance;
uniform float uFlowers;        // share of blades that are wildflowers (0..1)
uniform vec4 uBenders[4];      // things the grass bends away from: position, radius (0 = unused)

out vec3 vWorldPos;
out vec3 vNormal;
out float vHeight;             // 0 root .. 1 tip
out float vSeed;
out float vPatch;              // 0 dry .. 1 lush, a slow change across the field
out float vGust;               // how hard the wind is pressing this blade (for the sheen of wind waves)
out float vFlower;             // > 0: this blade is a flower of that kind (1, 2, 3)

float grassHash(float n) { return fract(sin(n) * 43758.5453); }
float grassHash2(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

// Smooth value noise on the ground plane, 0..1.
float grassNoise(vec2 p)
{
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(grassHash2(i), grassHash2(i + vec2(1.0, 0.0)), u.x),
               mix(grassHash2(i + vec2(0.0, 1.0)), grassHash2(i + vec2(1.0, 1.0)), u.x), u.y);
}

void main()
{
    float seed = aInstance.w;
    vec3 root = aInstance.xyz;

    // Thin out with distance: each blade has its own distance at which it goes (no hard line where grass ends).
    float dist = distance(root, uViewPos);
    float keep = 1.0 - smoothstep(uMaxDistance * 0.25, uMaxDistance, dist);
    if (grassHash(seed * 91.7) > keep)
    {
        gl_Position = vec4(0.0, 0.0, 2.0, 1.0);   // outside the view: the blade is not drawn
        return;
    }

    // Patches across the field: lush, tall and dark in some places, drier, shorter and yellower in others.
    float lushness = grassNoise(root.xz * 0.11) * 0.7 + grassNoise(root.xz * 0.37 + 5.3) * 0.3;
    float flower = grassHash(seed * 57.3) < uFlowers ? 1.0 + floor(grassHash(seed * 19.9) * 3.0) : 0.0;

    // Its own shape: height, width, facing and natural lean.
    float height = uGrassHeight * (0.55 + 0.9 * grassHash(seed * 13.1)) * mix(0.65, 1.25, lushness);
    if (flower > 0.0) height = uGrassHeight * (0.7 + 0.3 * grassHash(seed * 2.9));   // flowers stand a bit above the lawn
    float width = uGrassWidth * (0.7 + 0.6 * grassHash(seed * 7.3));
    float facing = grassHash(seed * 3.9) * 6.2831853;
    vec3 across = vec3(cos(facing), 0.0, sin(facing));
    // Turn partly towards the camera, so blades never vanish edge-on into thin lines.
    vec3 toEye = uViewPos - root;
    vec3 side = normalize(vec3(-toEye.z, 0.0, toEye.x) + vec3(1e-4, 0.0, 0.0));
    across = normalize(mix(across, side * sign(dot(across, side) + 1e-4), 0.45));
    vec2 lean = vec2(cos(facing * 1.7 + 1.0), sin(facing * 1.7 + 1.0)) * (0.1 + 0.25 * grassHash(seed * 5.5));

    // Wind: a travelling gust wave over the field plus each blade's own flutter.
    float t = aBlade.y;
    vec2 windDir = length(uWind) > 1e-4 ? normalize(uWind) : vec2(1.0, 0.0);
    float wave = dot(root.xz, windDir) * 0.35 - uWeatherTime * uWindSpeed;
    float gust = (sin(wave) * 0.5 + 0.5) * (0.6 + 0.4 * grassNoise(root.xz * 0.05 - windDir * uWeatherTime * 0.4));
    float flutter = sin(uWeatherTime * uWindSpeed * 3.1 + seed * 40.0) * 0.15;
    vec2 bend = lean + uWind * (0.35 + gust * 1.2 + flutter);

    // Pushed aside by the player (and anything else listed) walking through.
    for (int i = 0; i < 4; ++i)
    {
        vec4 b = uBenders[i];
        if (b.w <= 0.0) continue;
        vec2 away = root.xz - b.xz;
        float d = length(away);
        float push = (1.0 - smoothstep(b.w * 0.3, b.w, d)) * step(abs(root.y - b.y), 2.5);
        bend += away / max(d, 1e-3) * push * 1.4;
    }

    // Bend more towards the tip, and sink a little so the blade keeps its length.
    float curve = t * t;
    vec3 position = root + across * aBlade.x * width * (1.0 - t * 0.9) + vec3(0.0, t * height, 0.0);
    position.xz += bend * height * curve;
    position.y -= min(length(bend), 1.2) * height * curve * 0.35;

    // A rounded blade: the normal turns across its width, so it shades like a leaf, not a flat strip.
    vec3 face = normalize(cross(across, vec3(bend.x, 1.0, bend.y)));
    vec3 rounded = normalize(face + across * aBlade.x * 1.6);
    vNormal = normalize(mix(vec3(0.0, 1.0, 0.0), rounded, 0.5));
    vWorldPos = position;
    vHeight = t;
    vSeed = seed;
    vPatch = lushness;
    vGust = gust * length(uWind);
    vFlower = flower;
    gl_Position = uProjection * uView * vec4(position, 1.0);
}
