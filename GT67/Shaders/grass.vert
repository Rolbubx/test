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
uniform vec4 uBenders[4];      // things the grass bends away from: position, radius (0 = unused)

out vec3 vWorldPos;
out vec3 vNormal;
out float vHeight;             // 0 root .. 1 tip
out float vSeed;

float grassHash(float n) { return fract(sin(n) * 43758.5453); }

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

    // Its own shape: height, width, facing and natural lean.
    float height = uGrassHeight * (0.55 + 0.9 * grassHash(seed * 13.1));
    float width = uGrassWidth * (0.7 + 0.6 * grassHash(seed * 7.3));
    float facing = grassHash(seed * 3.9) * 6.2831853;
    vec3 across = vec3(cos(facing), 0.0, sin(facing));
    vec2 lean = vec2(cos(facing * 1.7 + 1.0), sin(facing * 1.7 + 1.0)) * (0.1 + 0.25 * grassHash(seed * 5.5));

    // Wind: a travelling gust wave over the field plus each blade's own flutter.
    float t = aBlade.y;
    vec2 windDir = length(uWind) > 1e-4 ? normalize(uWind) : vec2(1.0, 0.0);
    float gust = sin(dot(root.xz, windDir) * 0.35 - uWeatherTime * uWindSpeed) * 0.5 + 0.5;
    float flutter = sin(uWeatherTime * uWindSpeed * 3.1 + seed * 40.0) * 0.15;
    vec2 bend = lean + uWind * (0.35 + gust + flutter);

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

    // Lit mostly from above (soft, like a lawn), tilted towards the blade's face.
    vec3 face = normalize(cross(across, vec3(bend.x, 1.0, bend.y)));
    vNormal = normalize(mix(vec3(0.0, 1.0, 0.0), face, 0.35));
    vWorldPos = position;
    vHeight = t;
    vSeed = seed;
    gl_Position = uProjection * uView * vec4(position, 1.0);
}
