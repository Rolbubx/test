in vec2 vUV;
out vec4 FragColor;

uniform sampler2D uInput;
uniform sampler2D uDepth;
uniform mat4 uInvViewProj;
uniform vec3 uCameraPos;
uniform vec3 uFront;
uniform vec2 uTexel;
uniform float uNear;
uniform float uFar;
uniform float uTime;
uniform float uIntensity;   // 0..1
uniform float uSpeed;       // fall speed, m/s
uniform float uLength;      // streak length, m
uniform float uSlant;       // horizontal drift per metre of fall (wind)
uniform float uBrightness;
uniform float uDarken;      // how much the scene dims and greys at full intensity
uniform vec3 uSunDir;       // towards the sun: rain is brightest when lit from behind
uniform vec3 uTint;         // colour of the light the drops catch (linear)
uniform float uSplashes;    // brightness of the splashes where drops hit the ground
uniform int uLayers;         // streak layers drawn (quality)

const int LAYERS = 5;
const float PERIOD = 5.0;   // vertical spacing of drops in a column, m

float hash11(float p)
{
    p = fract(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return fract(p);
}

float hash21(vec2 p)
{
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float linearDepth(float d)
{
    float z = d * 2.0 - 1.0;
    return 2.0 * uNear * uFar / (uFar + uNear - z * (uFar - uNear));
}

// Rain on a cylinder of radius R around the camera: columns of falling drops, 0..1
float rainLayer(vec3 dir, float R, float sceneDepth, float pixelAngle, float seed)
{
    float horiz = length(dir.xz);
    if (horiz < 1e-3) return 0.0;
    float t = R / horiz;                               // distance along the ray to the cylinder
    if (t * dot(dir, uFront) > sceneDepth) return 0.0; // hidden behind geometry

    float cell = 0.1 + R * 0.03;                       // wider columns further out keep the density steady
    float u = atan(dir.z, dir.x) * R;                  // arc length around the cylinder
    float v = dir.y * t;                               // height

    float col = floor(u / cell);
    float h = hash11(col * 1.37 + seed * 17.0);
    float h2 = hash11(col * 3.11 + seed * 5.0);
    if (h2 > 0.72) return 0.0;                          // not every column has rain

    // Every column is a different drop: size changes its length, thickness, brightness and speed (big drops fall faster)
    float size = 0.55 + 0.9 * h;
    float len = uLength * (0.6 + 0.8 * hash11(col * 7.7 + seed));
    float fall = uTime * uSpeed * (0.8 + 0.35 * size);
    float along = fract((v + fall + h * PERIOD) / PERIOD) * PERIOD;   // distance behind the drop head, m
    float tail = clamp(along / len, 0.0, 1.0);
    float body = pow(1.0 - tail, 1.6) * step(along, len);                // bright head, long fading tail

    // Wind: shear the column across its width, gusting a little with time and height
    float gust = 1.0 + 0.45 * sin(uTime * 0.6 + v * 0.05 + seed);
    float x = fract(u / cell + uSlant * gust * v / cell + h) - 0.5;
    float pixelWorld = t * pixelAngle;                 // world size of one pixel here
    float width = max(0.007, pixelWorld * 0.7) * size * mix(1.0, 0.35, tail);   // fat head, thin tail
    // Drops close to the camera are out of focus: wide, soft and faint. Far ones are crisp.
    float nearness = 1.0 - clamp((R - 1.0) / 5.0, 0.0, 1.0);
    width *= 1.0 + 2.2 * nearness;
    float line = smoothstep(width / cell, width / cell * mix(0.3, 0.0, nearness), abs(x));
    float coverage = clamp(0.008 / width, 0.0, 1.0);   // thin far streaks fade instead of flickering

    // A glint where the drop catches the light: a short bright dot at the head of some drops
    float glint = smoothstep(0.05, 0.0, tail) * step(0.82, h2 + h * 0.3) * 1.8;
    float focus = mix(1.0, 0.45, nearness);
    return body * line * coverage * focus * (0.3 + 0.7 * size * 0.8) * (1.0 + glint);
}

// Tiny crowns where drops hit the ground: bright blobs that bloom and fade in about 0.25 s
float groundSplashes(vec3 wp, vec3 normal, float dist)
{
    if (normal.y < 0.9 || dist > 18.0) return 0.0;
    const float CELL = 0.32;
    vec2 g = wp.xz / CELL;
    vec2 id = floor(g);
    float sum = 0.0;
    for (int j = -1; j <= 1; ++j)
        for (int i = -1; i <= 1; ++i)
        {
            vec2 c = id + vec2(float(i), float(j));
            float h = hash21(c);
            if (h > 0.5) continue;
            float period = 0.35 + 0.5 * hash21(c + 4.2);
            float age = fract(uTime / period + hash21(c + 8.8)) * period;
            if (age > 0.25) continue;
            vec2 centre = (c + vec2(hash21(c + 1.1), hash21(c + 2.3))) * CELL;
            float d = length(wp.xz - centre);
            float radius = 0.015 + age * 0.22;
            float ring = smoothstep(0.01, 0.002, abs(d - radius)) + smoothstep(0.02, 0.0, d) * (1.0 - age / 0.25);
            sum += ring * (1.0 - age / 0.25) * (0.5 + 0.5 * h);
        }
    return sum;
}

void main()
{
    vec3 color = texture(uInput, vUV).rgb;
    if (uIntensity <= 0.0) { FragColor = vec4(color, 1.0); return; }

    float depth = texture(uDepth, vUV).r;
    bool isSky = depth >= 0.99999;
    float sceneDepth = isSky ? 1e6 : linearDepth(depth);

    vec4 far = uInvViewProj * vec4(vUV * 2.0 - 1.0, 1.0, 1.0);
    vec3 dir = normalize(far.xyz / far.w - uCameraPos);
    float pixelAngle = length(uTexel) * 1.2;

    // Gloom: a light dim and desaturation (the real distance fog now comes from the world shaders)
    float lum = dot(color, vec3(0.2126, 0.7152, 0.0722));
    color = mix(color, vec3(lum), 0.35 * uDarken * uIntensity);
    color *= 1.0 - 0.3 * uDarken * uIntensity;

    // Falling streaks, near to far. Drops refract the light behind them, so they brighten what is
    // behind and add a little of the light they catch.
    float streaks = 0.0;
    float ratio = pow(25.0 / 1.3, 1.0 / float(max(uLayers - 1, 1)));
    for (int i = 0; i < LAYERS; ++i)
    {
        if (i >= uLayers) break;
        float R = 1.3 * pow(ratio, float(i));            // 1.3 ... 25 m
        float farness = float(i) / float(max(uLayers - 1, 1));
        streaks += rainLayer(dir, R, sceneDepth, pixelAngle, float(i)) * mix(1.0, 0.55, farness);
    }

    streaks *= uIntensity * (0.7 + 0.8 * pow(max(dot(dir, uSunDir), 0.0), 3.0));   // backlit rain shows best
    color *= 1.0 + streaks * 1.2 * uBrightness;
    color += uTint * streaks * 0.8 * uBrightness;

    // Splashes on flat ground, found from the screen-space normal of the reconstructed surface
    if (!isSky && uSplashes > 0.0)
    {
        vec3 wp = uCameraPos + dir * (sceneDepth / max(dot(dir, uFront), 1e-3));
        vec3 normal = normalize(cross(dFdx(wp), dFdy(wp)));
        if (normal.y < 0.0) normal = -normal;
        color += uTint * groundSplashes(wp, normal, sceneDepth) * uSplashes * uIntensity;
    }

    FragColor = vec4(color, 1.0);
}
