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
vec3 waveNormal(vec2 xz, out float height, out float foam)
{
    vec2 largeUv = (CASCADE_TURN * (xz * CASCADE_SCALE) + 0.5 * uPatchSize) / uPatchSize;
    vec2 detailUv = xz / uPatchSize;
    vec2 slopeLarge = transpose(CASCADE_TURN) * texture(uOceanSlope, largeUv).rg * CASCADE_SCALE;   // chain rule
    vec2 slopeDetail = texture(uOceanSlope, detailUv).rg * DETAIL_WEIGHT;
    vec4 large = texture(uOceanDisplacement, largeUv);
    vec4 detail = texture(uOceanDisplacement, detailUv);
    height = (large.r + detail.r * DETAIL_WEIGHT) / (uWaveHeight * 1.2);
    foam = max(large.a, detail.a);
    vec2 slope = slopeLarge + slopeDetail;
    return normalize(vec3(-slope.x, 1.0, -slope.y));
}

// ---- rain drops hitting the surface ---------------------------------------------------------------
uniform float uRain;   // 0..1


float rainHash(vec2 p)
{
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

// Slope (dh/dx, dh/dz) of the expanding rings that falling drops leave on a flat water surface.
// rate = fraction of the ground cells that get drops (0..1).
vec2 rainRipples(vec2 p, float t, float rate)
{
    const float CELL = 0.55;
    vec2 id = floor(p / CELL);
    vec2 slope = vec2(0.0);
    for (int j = -1; j <= 1; ++j)
        for (int i = -1; i <= 1; ++i)
        {
            vec2 c = id + vec2(float(i), float(j));
            if (rainHash(c) > rate) continue;
            float period = 0.8 + 0.7 * rainHash(c + 7.1);
            float age = fract(t / period + rainHash(c + 3.3)) * period;       // seconds since the drop hit
            vec2 centre = (c + vec2(rainHash(c + 1.7), rainHash(c + 9.2))) * CELL;
            vec2 d = p - centre;
            float dist = length(d);
            // Capillary waves outrun the gravity waves: a sharp, tightly packed ring leads, a softer one trails
            float x1 = dist - age * 0.75;
            float x2 = dist - age * 0.48;
            float fade = exp(-age * 3.5) * smoothstep(0.0, 0.03, age);
            float w1 = exp(-x1 * x1 * 1800.0) * cos(x1 * 95.0);
            float w2 = exp(-x2 * x2 * 500.0) * cos(x2 * 55.0) * 0.55;
            slope += d / max(dist, 1e-4) * (w1 + w2) * fade;
        }
    return slope * 0.7;
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
    slope += rainRipples(p, uTime, uRain) * 0.8;
    float splashFoam;
    slope += splashRipples(p, splashFoam);
    vec3 N = normalize(vec3(waveN.x + slope.x, waveN.y, waveN.z + slope.y));

    vec3 V = normalize(uViewPos - vWorldPos);
    float NdotV = max(dot(N, V), 0.0);
    float fresnel = uFresnelF0 + (1.0 - uFresnelF0) * pow(1.0 - NdotV, 5.0);

    // Screen-space distortion shrinks with distance: ripples a metre away bend the image a lot, ripples far out
    // barely at all, so distant water stays a sharp mirror instead of a smeared blur.
    float distortionFade = 1.0 / (1.0 + viewDistance * 0.06);

    // Refraction: look through the surface, bent by the ripples. Never pull in anything in front of the water.
    vec2 refractUV = uv + N.xz * uRefractionStrength * distortionFade;
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

    // Soft shore: fade into the untouched scene where the water gets thin.
    float edge = uEdgeSoftness > 0.0 ? smoothstep(0.0, uEdgeSoftness, thickness) : 1.0;
    color = mix(texture(uSceneColor, uv).rgb, color, edge);

    // Foam on crests and where the water meets the shore or a submerged object.
    float pattern = texture(uRippleMap, p * 0.45 + flow * 0.6).a;
    float pattern2 = texture(uRippleMap, p * 1.1 - flow).a;
    float crest = breaking;   // where the FFT waves fold over: real whitecaps, not just "high points"
    float shore = 1.0 - smoothstep(0.0, max(uShoreFoam, 0.01), thickness);
    // Waves lapping at the shore: foam bands that roll in towards the water's edge (their crests move to thinner
    // water as time goes on), uneven along the shore.
    float shoreWaves = 1.0 - smoothstep(0.0, max(uShoreFoam, 0.01) * 1.6, thickness);
    float bands = 0.5 + 0.5 * sin(thickness * 9.0 / max(uShoreFoam, 0.05) + uTime * 1.6 + pattern * 6.0);
    float foam = (crest * 0.5 + shore * shore * 1.4 + shoreWaves * smoothstep(0.8, 0.97, bands) * 0.45 + splashFoam) * uFoamAmount;
    foam *= smoothstep(0.46, 0.54, pattern * 0.6 + pattern2 * 0.4);   // crisp foam edges, not a grey haze
    vec3 foamColor = (uAmbient + uSunColor * max(uSunDir.y, 0.0) * 0.8) * 0.9;
    color = mix(color, foamColor, clamp(foam, 0.0, 1.0));

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
