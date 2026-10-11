in vec3 vWorldPos;
in vec2 vBaseXZ;
in vec4 vClip;
in float vWaveHeight;

out vec4 FragColor;

uniform sampler2D uReflection;   // scene + sky mirrored in the water plane
uniform sampler2D uSceneColor;   // opaque scene without the water
uniform sampler2D uSceneDepth;
uniform sampler2D uRippleMap;    // rg = slope, a = height

uniform vec3 uViewPos;
uniform float uTime;
uniform float uNear;
uniform float uFar;

uniform vec3 uSunDir;            // unit vector towards the sun
uniform vec3 uSunColor;          // linear, includes intensity and atmosphere
uniform vec3 uAmbient;

uniform vec3 uShallowColor;
uniform vec3 uDeepColor;
uniform vec3 uAbsorption;
uniform float uDepthColorRate;
uniform vec2 uWind;
uniform float uRippleStrength;
uniform float uRippleScale;
uniform bool uReflect;
uniform float uReflectionDistortion;
uniform float uRefractionStrength;
uniform float uFresnelF0;
uniform float uSpecularStrength;
uniform float uShininess;
uniform float uSparkle;          // tight, very bright glints on the small ripples
uniform float uCrestLight;       // sun shining through the thin tops of the waves
uniform float uFoamAmount;
uniform float uShoreFoam;
uniform float uEdgeSoftness;

uniform float uFogDensity;   // per metre
uniform vec3 uFogColor;

uniform vec4 uWaterRect;         // xy = centre (x, z), zw = half size
uniform float uEdgeFade;         // metres over which the water fades out at its outer border (0 = hard edge)

// FFT ocean (OceanWaves), sampled like in the vertex shader: the patch as is, and 2.7x larger turned by 34 degrees.
uniform sampler2D uOceanDisplacement;   // r = height, gb = sideways displacement, a = foam
uniform sampler2D uOceanSlope;          // rg = slope of the height along x and z
uniform float uPatchSize;
uniform float uWaveHeight;

// Splashes and wakes (Water.Splash): x, z of the centre, start time (water time), strength (0 = unused).
#define MAX_SPLASHES 24
uniform vec4 uSplashes[MAX_SPLASHES];

// Slope of the rings spreading from every splash: a short train of crests behind a front moving out at 1.4 m/s,
// fading with time and distance. foam: white water where something just hit the surface.
vec2 splashRipples(vec2 p, out float foam)
{
    vec2 slope = vec2(0.0);
    foam = 0.0;
    for (int i = 0; i < MAX_SPLASHES; ++i)
    {
        vec4 s = uSplashes[i];
        if (s.w <= 0.0) continue;
        float age = uTime - s.z;
        if (age < 0.0 || age > 4.0) continue;
        vec2 d = p - s.xy;
        float dist = length(d);
        float x = dist - age * 1.4;                         // distance behind the front
        if (x > 0.4 || x < -3.0) continue;
        float train = exp(-(x + 0.7) * (x + 0.7) * 1.3);    // a few crests just behind the front
        float amplitude = s.w * exp(-age * 0.8) / (1.0 + dist * 0.7);
        slope += d / max(dist, 1e-3) * cos(x * 9.0) * train * amplitude * 0.9;
        foam += s.w * (smoothstep(0.7, 0.0, dist) * exp(-age * 2.2) + train * exp(-age * 1.6) * 0.15);
    }
    return slope;
}

const float CASCADE_SCALE = 0.37;
const mat2 CASCADE_TURN = mat2(0.825336, 0.564642, -0.564642, 0.825336);
const float DETAIL_WEIGHT = 0.2;   // the short-wave copy is lower, as shorter waves are on real water

// The wave normal, height and foam per pixel, from the FFT textures: the lighting does not depend on how many vertices
// the tile has (distant tiles do not look faceted), and the mipmaps average away waves smaller than a pixel.
// height: about -1 trough .. 1 crest.
// A third, smaller copy of the waves (2.7x smaller, turned the other way) adds the fine chop real water always has,
// so the surface glitters and breaks up reflections even with the ripple layers off. The slope of each smaller
// copy is about as strong as the one above it (short waves are lower, but just as steep).
const mat2 MICRO_TURN = mat2(0.309017, -0.951057, 0.951057, 0.309017);   // -72 degrees
const float MICRO_SCALE = 2.7;

vec3 waveNormal(vec2 xz, out float height, out float foam)
{
    vec2 largeUv = (CASCADE_TURN * (xz * CASCADE_SCALE) + 0.5 * uPatchSize) / uPatchSize;
    vec2 detailUv = xz / uPatchSize;
    vec2 microUv = (MICRO_TURN * (xz * MICRO_SCALE) + 0.25 * uPatchSize) / uPatchSize;
    vec2 slopeLarge = transpose(CASCADE_TURN) * texture(uOceanSlope, largeUv).rg * CASCADE_SCALE;   // chain rule
    vec2 slopeDetail = texture(uOceanSlope, detailUv).rg * DETAIL_WEIGHT;
    // Fades out with distance (and in its mip levels) where it could only shimmer.
    float microFade = exp(-length(uViewPos - vWorldPos) * 0.035);
    vec2 slopeMicro = transpose(MICRO_TURN) * texture(uOceanSlope, microUv).rg * DETAIL_WEIGHT * microFade;
    vec4 large = texture(uOceanDisplacement, largeUv);
    vec4 detail = texture(uOceanDisplacement, detailUv);
    height = (large.r + detail.r * DETAIL_WEIGHT) / (uWaveHeight * 1.2);
    foam = max(large.a, detail.a);
    // The same slow wind patches as the ripples: some areas choppier than others, ripples on or off.
    float gust = 0.45 + 1.1 * smoothstep(0.25, 0.75, texture(uRippleMap, xz * 0.011 + normalize(uWind + 1e-5) * uTime * 0.0048).a);
    vec2 slope = slopeLarge + slopeDetail + slopeMicro * gust;
    return normalize(vec3(-slope.x, 1.0, -slope.y));
}

vec2 hash22(vec2 p)
{
    vec3 q = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
    q += dot(q, q.yzx + 33.33);
    return fract((q.xx + q.yz) * q.zy);
}

// ---- rain drops hitting the surface ---------------------------------------------------------------
uniform float uRain;   // 0..1

// One layer of drops: now and then a cell of the grid gets a drop, which sends out a ring. The ring is a short train
// of capillary waves: it spreads as it travels (its waves get longer and the train wider) and dies out within a
// second. footprint = metres per pixel: rings finer than a couple of pixels fade instead of shimmering.
// splash: a tiny white fleck where a drop has just hit.
vec2 rainLayer(vec2 p, float t, float rate, float cell, float seed, float footprint, inout float splash)
{
    vec2 id = floor(p / cell);
    vec2 slope = vec2(0.0);
    for (int j = -1; j <= 1; ++j)
        for (int i = -1; i <= 1; ++i)
        {
            vec2 c = id + vec2(float(i), float(j));
            vec2 h = hash22(c + seed);
            if (h.x > rate) continue;
            vec2 h2 = hash22(c + seed + 17.0);
            float period = 0.7 + 0.6 * h.y;
            float age = fract(t / period + h2.x) * period;                    // seconds since the drop hit
            vec2 centre = (c + 0.15 + 0.7 * hash22(c + seed + 31.0)) * cell;
            vec2 d = p - centre;
            float dist = length(d);
            float x = dist - age * 0.5;                                       // distance from the ring's front
            float width = 0.01 + age * 0.045;
            float train = exp(-x * x / (width * width));
            float k = 6.2831853 / (0.03 + age * 0.04);                        // wave number of the ring's ripples
            float sharp = clamp(1.0 - footprint * k * 0.3, 0.0, 1.0);
            float strength = (0.5 + 0.5 * h2.y) * exp(-age * 2.2) * smoothstep(0.0, 0.015, age) / (1.0 + dist * 2.5);
            // Height 1.5 mm * train * cos(k x); its slope along the radius:
            slope += d / max(dist, 1e-4) * (-0.0018 * k * sin(k * x)) * train * strength * sharp;
            splash += smoothstep(0.02, 0.0, dist) * exp(-age * 30.0) * (0.5 + 0.5 * h2.y);
        }
    return slope;
}

// Two layers of drops (big and small), as many as the rain amount asks for.
vec2 rainRipples(vec2 p, float t, float rate, float footprint, out float splash)
{
    splash = 0.0;
    if (rate <= 0.0) return vec2(0.0);
    vec2 slope = rainLayer(p, t, rate, 0.45, 0.0, footprint, splash);
    slope += rainLayer(p, t * 1.13, rate, 0.28, 57.0, footprint, splash) * 0.6;
    return slope;
}

// ---- foam ---------------------------------------------------------------------------------------------
// Distance to the nearest wall between bubbles (0 on a wall, about 0.5 in the middle of a bubble). The bubbles drift
// slowly, so the foam never looks frozen.
float bubbleWalls(vec2 p, float t)
{
    vec2 id = floor(p), f = fract(p);
    float d1 = 8.0, d2 = 8.0;
    for (int j = -1; j <= 1; ++j)
        for (int i = -1; i <= 1; ++i)
        {
            vec2 g = vec2(float(i), float(j));
            vec2 o = 0.5 + 0.38 * sin(t * 0.5 + 6.2831853 * hash22(id + g));
            vec2 r = g + o - f;
            float d = dot(r, r);
            if (d < d1) { d2 = d1; d1 = d; }
            else if (d < d2) d2 = d;
        }
    return sqrt(d2) - sqrt(d1);
}

// How much of a pixel is foam, for a foam density (0 = none, 1 = thick). Thick foam is solid; thinner foam breaks up
// into a lace of bubble walls with clear water in the holes, at two bubble sizes. Edges are anti-aliased with the
// pixel footprint (metres per pixel); far away, where the bubbles are smaller than a pixel, only their cover remains.
float foamCover(vec2 p, vec2 flow, float density, float footprint)
{
    if (density < 0.005) return 0.0;
    density = min(density, 1.2);
    float far = smoothstep(0.025, 0.08, footprint);
    float haze = clamp(density * 0.6, 0.0, 1.0);
    if (far >= 1.0) return haze;

    vec2 q = p + flow * 0.5 + (texture(uRippleMap, p * 0.27).rg - 0.5) * 0.6;   // warped, so cells are uneven
    float big = bubbleWalls(q * 2.6, uTime);
    float small = bubbleWalls(q * 6.5 + 7.3, uTime * 1.4);
    float aaBig = max(footprint * 2.6 * 1.5, 0.015);
    float aaSmall = max(footprint * 6.5 * 1.5, 0.015);
    float wallBig = density * 0.5 - 0.06;
    float wallSmall = density * 0.42 - 0.04;
    float lace = max(1.0 - smoothstep(wallBig - aaBig, wallBig + aaBig, big),
                     (1.0 - smoothstep(wallSmall - aaSmall, wallSmall + aaSmall, small)) * 0.85);
    // A faint milky film under the lace, so thin foam does not look cut out.
    lace = max(lace, haze * 0.12);
    return mix(lace, haze, far);
}

float linearDepth(float d)
{
    float z = d * 2.0 - 1.0;
    return 2.0 * uNear * uFar / (uFar + uNear - z * (uFar - uNear));
}

void main()
{
    vec2 uv = vClip.xy / vClip.w * 0.5 + 0.5;
    float fragDepth = linearDepth(gl_FragCoord.z);

    float viewDistance = length(uViewPos - vWorldPos);

    // Surface normal: the waves (per pixel) plus scrolling ripple layers. The layers are turned at odd angles to each
    // other so their tiling never lines up into a grid; a third, fine layer adds crisp detail close to the camera.
    // Large slow patches make some areas rougher than others, like gusts of wind on a real lake.
    // Outside the water's rectangle (the larger tiles of the quadtree can reach past it): nothing.
    vec2 inside = uWaterRect.zw - abs(vBaseXZ - uWaterRect.xy);
    if (min(inside.x, inside.y) < 0.0) discard;

    float crestHeight, breaking;
    vec3 waveN = waveNormal(vBaseXZ, crestHeight, breaking);
    vec2 p = vWorldPos.xz;
    vec2 flow = normalize(uWind + 1e-5) * uTime * 0.04;
    const mat2 turnA = mat2(0.799, 0.602, -0.602, 0.799);    // 37 degrees
    const mat2 turnB = mat2(0.485, -0.875, 0.875, 0.485);    // -61 degrees
    vec4 r1 = texture(uRippleMap, p * uRippleScale + flow);
    vec4 r2 = texture(uRippleMap, (turnA * p) * uRippleScale * 2.3 - flow * 1.6 + 0.37);
    vec4 r3 = texture(uRippleMap, (turnB * p) * uRippleScale * 5.7 + flow * 2.4 + 0.71);
    float nearDetail = exp(-viewDistance * 0.05);
    float gust = 0.45 + 1.1 * smoothstep(0.25, 0.75, texture(uRippleMap, p * 0.011 + flow * 0.12).a);
    vec2 slope = ((r1.xy * 2.0 - 1.0)
                + transpose(turnA) * (r2.xy * 2.0 - 1.0) * 0.6
                + transpose(turnB) * (r3.xy * 2.0 - 1.0) * 0.45 * nearDetail) * uRippleStrength * gust;
    float footprint = length(fwidth(p));   // metres per pixel
    float rainSplash;
    slope += rainRipples(p, uTime, uRain, footprint, rainSplash);
    float splashFoam;
    slope += splashRipples(p, splashFoam);
    vec3 N = normalize(vec3(waveN.x + slope.x, waveN.y, waveN.z + slope.y));

    vec3 V = normalize(uViewPos - vWorldPos);
    float NdotV = max(dot(N, V), 0.0);
    float fresnel = uFresnelF0 + (1.0 - uFresnelF0) * pow(1.0 - NdotV, 5.0);

    // Screen-space distortion shrinks with distance: ripples a metre away bend the image a lot, ripples far out
    // barely at all, so distant water stays a sharp mirror instead of a smeared blur.
    float distortionFade = 1.0 / (1.0 + viewDistance * 0.06);

    // Depth of the water straight down, from the depth buffer without the refraction's bending (bent depth tears the
    // shore line and the foam into jagged bits): the view ray's length through the water times how steeply it falls.
    float depthBelow = max(linearDepth(texture(uSceneDepth, uv).r) - fragDepth, 0.0) * clamp(V.y, 0.05, 1.0);

    // Refraction: look through the surface, bent by the ripples. The bend grows with the depth, as the light travels
    // further under the tilted surface, so shallow water at the shore stays clear instead of shredding the bed into
    // jagged patches. Never pull in anything in front of the water.
    vec2 refractUV = uv + N.xz * uRefractionStrength * distortionFade * smoothstep(0.0, 0.6, depthBelow);
    if (linearDepth(texture(uSceneDepth, refractUV).r) < fragDepth) refractUV = uv;
    refractUV = clamp(refractUV, vec2(0.001), vec2(0.999));

    float sceneDepth = linearDepth(texture(uSceneDepth, refractUV).r);
    float thickness = clamp(sceneDepth - fragDepth, 0.0, 60.0);   // water along the view ray
    vec3 sceneColor = texture(uSceneColor, refractUV).rgb;

    // Light that reaches the water body (sky + a share of the sun) colours the in-scattered water.
    vec3 bodyLight = uAmbient + uSunColor * max(uSunDir.y, 0.0) * 0.35;
    vec3 body = mix(uShallowColor, uDeepColor, 1.0 - exp(-thickness * uDepthColorRate)) * bodyLight;
    vec3 transmittance = exp(-uAbsorption * thickness);
    vec3 refracted = sceneColor * transmittance + body * (1.0 - transmittance);

    // Reflection of the mirrored scene (or a flat sky-coloured fallback).
    vec3 reflection = uAmbient * 2.5;
    if (uReflect)
    {
        vec2 reflectUV = clamp(uv + N.xz * uReflectionDistortion * distortionFade, vec2(0.001), vec2(0.999));
        reflection = texture(uReflection, reflectUV).rgb;
    }

    vec3 color = mix(refracted, reflection, fresnel);

    // Sun glint: a broad sheen plus pin-point sparkles where single ripples face the sun exactly.
    vec3 H = normalize(uSunDir + V);
    float NdotH = max(dot(N, H), 0.0);
    float glint = pow(NdotH, uShininess) * (uShininess + 8.0) / (8.0 * 3.14159265);
    float sparkleShininess = uShininess * 8.0;
    float sparkle = pow(NdotH, sparkleShininess) * (sparkleShininess + 8.0) / (8.0 * 3.14159265) * nearDetail * uSparkle;
    float sunUp = smoothstep(0.0, 0.08, uSunDir.y);
    color += uSunColor * (glint * 0.25 + sparkle * 0.06) * uSpecularStrength * sunUp;

    // Light through the waves: looking towards a low sun, the thin crests glow with the shallow colour.
    float towardsSun = pow(clamp(dot(V, -normalize(uSunDir + N * 0.6)), 0.0, 1.0), 4.0);
    float crestThin = smoothstep(0.0, 1.0, crestHeight * 0.5 + 0.5);
    color += uShallowColor * uSunColor * towardsSun * crestThin * (1.0 - fresnel) * 0.5 * uCrestLight * sunUp;

    // Soft shore: fade into the untouched scene where the water gets shallow.
    float edge = uEdgeSoftness > 0.0 ? smoothstep(0.0, uEdgeSoftness, depthBelow) : 1.0;
    color = mix(texture(uSceneColor, uv).rgb, color, edge);

    // Foam density: whitecaps where the FFT waves fold over, a solid line where the water touches the shore or a
    // submerged object, washes of foam rolling in towards the shore (bands that move to shallower water and fade as
    // they come, uneven along the shore), splashes and the flecks of raindrops.
    float w = max(uShoreFoam, 0.01);
    float n = texture(uRippleMap, p * 0.17 + flow * 0.3).a;
    float contact = 1.0 - smoothstep(0.0, w * 0.12, depthBelow);
    float phase = fract(depthBelow / w * 2.2 + uTime * 0.35 + n * 1.7);
    float wash = smoothstep(0.0, 0.06, phase) * exp(-phase * 5.0) * (1.0 - smoothstep(0.1 * w, 0.6 * w, depthBelow));
    float density = breaking * 0.6 + contact * contact * 1.1 + wash * smoothstep(0.3, 0.8, n) * 0.8
                  + splashFoam + rainSplash * 0.6 * uRain;
    float foam = foamCover(p, flow, density * uFoamAmount, footprint);

    // Foam is lit like a rough white surface (sun and sky), slightly see-through where it is thin.
    vec3 foamColor = (uAmbient * 1.15 + uSunColor * max(uSunDir.y, 0.0) * 0.75) * 0.92;
    color = mix(color, foamColor, clamp(foam, 0.0, 1.0) * 0.95);

    if (uFogDensity > 0.0)
        color = mix(color, uFogColor, 1.0 - exp(-viewDistance * uFogDensity * exp(-0.02 * max(vWorldPos.y, 0.0))));

    // Soft outer border: the water thins out over the last few metres of its rectangle instead of ending in a hard,
    // straight edge, so a sea does not look like a slab floating in front of the sky.
    if (uEdgeFade > 0.0)
    {
        // Far away a few metres are less than a pixel: the fade widens with distance so the far edge stays soft too.
        float border = smoothstep(0.0, max(uEdgeFade, viewDistance * 0.08), min(inside.x, inside.y));
        color = mix(texture(uSceneColor, uv).rgb, color, border);
    }

    FragColor = vec4(color, 1.0);
}
