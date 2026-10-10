#define MAX_POINT_LIGHTS 8

in VS_OUT
{
    vec3 worldPos;
    vec3 normal;
    vec3 tangent;
    vec2 uv;
    vec4 color;
    vec4 lightSpacePos;
    vec3 localPos;     // the model's own coordinates (for custom shaders)
    vec3 objectPos;    // the object's origin in the world
} fs;

out vec4 FragColor;

struct DirLight   { vec3 direction; vec3 color; };
struct PointLight { vec3 position; vec3 color; float range; };
struct SpotLight  { vec3 position; vec3 direction; vec3 color; float range; float cosInner; float cosOuter; };

// per frame
uniform vec3 uViewPos;
uniform vec3 uAmbient;
uniform bool uHasDirLight;
uniform DirLight uDirLight;
uniform int uPointLightCount;
uniform PointLight uPointLights[MAX_POINT_LIGHTS];
uniform bool uHasSpot;
uniform SpotLight uSpot;
uniform bool uShadowsEnabled;
uniform float uShadowBias;
uniform sampler2D uShadowMap;
uniform vec4 uHighlight; // a > 0: flat colour override (selection outline)

uniform float uFogDensity;   // per metre
uniform vec3 uFogColor;

// cloud shadows (same cloud field as sky.frag)
uniform bool uCloudShadows;
uniform sampler3D uCloudShape;
uniform float uCloudStorm;        // 0..1: a uniform storm sheet fills the gaps
uniform float uCloudMin;
uniform float uCloudMax;
uniform float uCloudCoverage;
uniform float uCloudDensity;
uniform float uCloudShapeScale;
uniform vec3 uCloudOffset;
uniform float uCloudShadowStrength;

uniform mat4 uLightSpace;   // world to the sun's shadow map (also used by lit.vert)
uniform float uShadowTexel; // world size of one shadow-map texel

// per object
uniform vec4 uColor;       // linear tint, a = opacity
uniform float uShininess;
uniform float uSpecularStrength;
uniform bool uUnlit;
uniform bool uReceiveShadow;
uniform bool uHasDiffuse;
uniform bool uHasSpecular;
uniform bool uHasNormal;
uniform sampler2D uDiffuseMap;
uniform sampler2D uSpecularMap;
uniform sampler2D uNormalMap;

// 3x3 PCF; returns 0 (lit) .. 1 (shadowed)
float shadowFactor(vec3 n, vec3 lightDir)
{
    // Normal-offset: look up the shadow from slightly above the surface, so the bias is small and edges do not stair-step
    float facing = clamp(dot(n, lightDir), 0.0, 1.0);
    vec4 lp = uLightSpace * vec4(fs.worldPos + n * uShadowTexel * 1.5 * (1.0 - facing), 1.0);
    vec3 p = lp.xyz / lp.w * 0.5 + 0.5;
    if (p.z > 1.0 || p.x < 0.0 || p.x > 1.0 || p.y < 0.0 || p.y > 1.0) return 0.0;

    float bias = max(uShadowBias * (1.0 - facing), uShadowBias * 0.2);
    vec2 texel = 1.0 / vec2(textureSize(uShadowMap, 0));

    // 16 taps of a Poisson disc, turned by a different angle at every pixel: a smooth penumbra with no stair-steps
    const vec2 DISC[16] = vec2[16](
        vec2(-0.94201624, -0.39906216), vec2(0.94558609, -0.76890725), vec2(-0.094184101, -0.92938870), vec2(0.34495938, 0.29387760),
        vec2(-0.91588581, 0.45771432), vec2(-0.81544232, -0.87912464), vec2(-0.38277543, 0.27676845), vec2(0.97484398, 0.75648379),
        vec2(0.44323325, -0.97511554), vec2(0.53742981, -0.47373420), vec2(-0.26496911, -0.41893023), vec2(0.79197514, 0.19090188),
        vec2(-0.24188840, 0.99706507), vec2(-0.81409955, 0.91437590), vec2(0.19984126, 0.78641367), vec2(0.14383161, -0.14100790));
    float angle = 6.2831853 * fract(52.9829189 * fract(dot(gl_FragCoord.xy, vec2(0.06711056, 0.00583715))));
    vec2 rot = vec2(cos(angle), sin(angle));
    float radius = 1.6;   // texels

    float shadow = 0.0;
    for (int i = 0; i < 16; ++i)
    {
        vec2 offset = vec2(DISC[i].x * rot.x - DISC[i].y * rot.y, DISC[i].x * rot.y + DISC[i].y * rot.x) * radius * texel;
        float depth = texture(uShadowMap, p.xy + offset).r;
        shadow += (p.z - bias > depth) ? 1.0 : 0.0;
    }
    return shadow / 16.0;
}

// Cloud density without the erosion detail (shadows are soft anyway); mirrors cloudDensity() in sky.frag.
// ---- random cloud weather (CloudLayer.Variation, Evolution, WeatherSize, Seed) -----------------------
uniform sampler2D uCloudWeather;   // periodic noise (alpha), generated from the cloud seed
uniform float uCloudTime;          // seconds
uniform float uCloudEvolution;     // how fast the weather and the shapes change by themselves
uniform float uCloudWeatherScale;  // 1 / metres of the weather pattern
uniform float uCloudVariation;     // 0..1: how much cover and height change from place to place

// 0..1: how much cloud the weather wants here. Two drifting copies of the noise: it never repeats and slowly changes.
float cloudWeather(vec2 xz)
{
    vec2 uv = xz * uCloudWeatherScale;
    float t = uCloudTime * uCloudEvolution * 0.0004;
    float a = texture(uCloudWeather, uv + vec2(t, t * 0.6)).a;
    float b = texture(uCloudWeather, uv * 1.73 + vec2(-t * 0.8, t * 1.1) + 0.37).a;
    return clamp(a * 0.65 + b * 0.35, 0.0, 1.0);
}

// The cloud shapes billow and change: the 3D noise is read at a slowly moving height.
vec3 cloudEvolve() { return vec3(0.0, uCloudTime * uCloudEvolution * 0.0015, 0.0); }

float cloudShadowDensity(vec3 p)
{
    float h = (p.y - uCloudMin) / (uCloudMax - uCloudMin);
    if (h <= 0.0 || h >= 1.0) return 0.0;

    // Random weather: how much cloud here, and how tall it grows (towers where it is thick, flat where thin).
    float weather = cloudWeather(p.xz + uCloudOffset.xz);
    float top = mix(1.0, 0.35 + 0.65 * weather, uCloudVariation);
    float profile = smoothstep(0.0, 0.12, h) * (1.0 - smoothstep(top * 0.55, top, h));
    float overcast = uCloudStorm * profile * 0.75;   // storm: an unbroken sheet
    vec3 q = (p + uCloudOffset) * uCloudShapeScale;
    vec4 n = textureLod(uCloudShape, q + cloudEvolve(), 2.5);
    float worleyFbm = n.g * 0.625 + n.b * 0.25 + n.a * 0.125;
    float baseShape = clamp((n.r - 0.35) / 0.65, 0.0, 1.0);
    float shape = baseShape * (0.4 + 0.6 * clamp(worleyFbm / 0.8, 0.0, 1.0));

    float coverage = clamp(uCloudCoverage + (weather - 0.5) * 1.4 * uCloudVariation, 0.0, 1.0);
    if (coverage <= 0.001) return overcast * uCloudDensity;

    float lo = 1.0 - coverage;
    float density = (shape * profile - lo) / coverage;
    return clamp(max(density, overcast), 0.0, 1.0) * uCloudDensity;
}

// 1 = open sky, towards 0 under a cloud: march from the surface to the sun through the cloud slab
float cloudShadowFactor(vec3 pos, vec3 toSun)
{
    if (uCloudStorm >= 0.97) return 0.6;   // solid overcast: nothing to march
    if (toSun.y < 0.02) return 1.0;
    float t0 = max((uCloudMin - pos.y) / toSun.y, 0.0);
    float t1 = (uCloudMax - pos.y) / toSun.y;
    if (t1 <= t0) return 1.0;

    const int STEPS = 5;
    float ds = (t1 - t0) / float(STEPS);
    float od = 0.0;
    for (int i = 0; i < STEPS; ++i)
        od += cloudShadowDensity(pos + toSun * (t0 + ds * (float(i) + 0.5))) * ds;

    float transmittance = exp(-od * 0.006);
    // A storm sheet still lets a little diffuse sun through
    return max(mix(1.0, transmittance, uCloudShadowStrength), uCloudStorm * 0.6);
}

// ---- rain on surfaces ----------------------------------------------------------------------------
uniform float uWetness;      // 0..1, surfaces darken, get glossy and form puddles
uniform float uRainAmount;   // 0..1, how many drops hit the puddles
uniform float uWeatherTime;
uniform float uFogReflect;      // fog density per metre, for fogging the mirrored image
uniform bool uPuddleMirror;
uniform sampler2D uPuddleReflection;
uniform vec2 uScreenSize;
uniform float uPuddleLevel;

float rainHash(vec2 p)
{
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float valueNoise(vec2 p)
{
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(rainHash(i), rainHash(i + vec2(1.0, 0.0)), f.x),
               mix(rainHash(i + vec2(0.0, 1.0)), rainHash(i + vec2(1.0, 1.0)), f.x), f.y);
}

float puddleNoise(vec2 p)
{
    // Mostly broad, rounded blobs (real puddles fill dips in the ground) with only a little fine wobble on the shore
    return valueNoise(p * 0.32) * 0.72 + valueNoise(p * 0.9 + 11.0) * 0.2 + valueNoise(p * 2.6 + 5.0) * 0.08;
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

// shin = shininess of the (possibly wet) surface; a sharper highlight is scaled up so it keeps its energy
vec3 blinnPhong(vec3 n, vec3 v, vec3 l, vec3 lightColor, vec3 albedo, vec3 specColor, float shin, float specFade)
{
    float diff = max(dot(n, l), 0.0);
    vec3 h = normalize(l + v);
    float spec = diff > 0.0 ? pow(max(dot(n, h), 0.0), shin) * min((shin + 8.0) / (uShininess + 8.0), 4.0) * specFade : 0.0;
    return lightColor * (albedo * diff + specColor * spec);
}

float rangeAttenuation(float dist, float range)
{
    float x = clamp(1.0 - pow(dist / range, 4.0), 0.0, 1.0);
    return x * x / (dist * dist + 1.0);
}

// ---- caustics: the light that waves focus onto everything under the water -----------------------------
#define MAX_CAUSTIC_WATERS 4
uniform int uCausticCount;
uniform vec4 uCausticWater[MAX_CAUSTIC_WATERS];   // centre x, z, half width, half depth
uniform vec4 uCausticInfo[MAX_CAUSTIC_WATERS];    // level, water time, strength, wave length

vec2 cstHash(vec2 p)
{
    p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
    return fract(sin(p) * 43758.5453);
}

// Bright where two cells meet: one layer of drifting, wobbling cells (the network of light lines under waves).
float cstLayer(vec2 q, float t)
{
    vec2 cell = floor(q), f = fract(q);
    float d1 = 8.0, d2 = 8.0;
    for (int j = -1; j <= 1; ++j)
    for (int i = -1; i <= 1; ++i)
    {
        vec2 g = vec2(float(i), float(j));
        vec2 o = 0.5 + 0.42 * sin(t * 0.9 + 6.2831 * cstHash(cell + g));
        vec2 r = g + o - f;
        float d = dot(r, r);
        if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) { d2 = d; }
    }
    return exp(-(sqrt(d2) - sqrt(d1)) * 9.0);
}

// Extra sunlight on a point under one of the waters (0 when it is above or outside every water).
float causticLight(vec3 p)
{
    float light = 0.0;
    for (int i = 0; i < uCausticCount; ++i)
    {
        vec4 rect = uCausticWater[i], info = uCausticInfo[i];
        float depth = info.x - p.y;
        if (depth <= 0.0 || info.z <= 0.0) continue;
        vec2 inside = rect.zw - abs(p.xz - rect.xy);
        if (min(inside.x, inside.y) < 0.0) continue;

        float t = info.y;
        vec2 q = p.xz / (0.6 + info.w * 0.25);          // cells about the size of the small waves
        float a = cstLayer(q + vec2(t * 0.11, -t * 0.07), t);
        float b = cstLayer(q * 1.37 + vec2(-t * 0.09, t * 0.13) + 3.7, t * 1.2);
        float pattern = pow(a * 0.65 + b * 0.65, 2.2);
        // Strongest just under the surface (fading in over the first few cm), dimmer with depth, gone at the shore edge.
        float fade = smoothstep(0.0, 0.12, depth) * exp(-depth * 0.35) * smoothstep(0.0, 0.5, min(inside.x, inside.y));
        light += pattern * fade * info.z * 0.9;
    }
    return light;
}

//@USER_DECLARATIONS@

void main()
{
    vec4 base = vec4(uColor.rgb, uColor.a) * vec4(fs.color.rgb, fs.color.a);
    if (uHasDiffuse)
        base *= texture(uDiffuseMap, fs.uv);

    if (uHighlight.a > 0.0)
    {
        FragColor = vec4(uHighlight.rgb, 1.0);
        return;
    }

    if (uUnlit)
    {
        FragColor = base;
        return;
    }

    vec3 albedo = base.rgb;
    vec3 n = normalize(fs.normal);
    if (!gl_FrontFacing) n = -n;
    if (uHasNormal)
    {
        vec3 t = normalize(fs.tangent - n * dot(n, fs.tangent));
        vec3 b = cross(n, t);
        vec3 tn = texture(uNormalMap, fs.uv).rgb * 2.0 - 1.0;
        n = normalize(mat3(t, b, n) * tn);
    }

    vec3 v = normalize(uViewPos - fs.worldPos);
    vec3 specColor = (uHasSpecular ? texture(uSpecularMap, fs.uv).rgb : vec3(1.0)) * uSpecularStrength;

    // Custom shaders (.shader files) fill these in; the plain lit shader leaves them at their defaults
    vec3 userEmission = vec3(0.0);
    float userShin = uShininess;
    float userSpec = 1.0;
    float userAlpha = 1.0;   // below 0.5 a custom shader cuts the pixel away
    //@USER_CALL@
    specColor *= userSpec;

    // Wet surfaces: darker, glossier, with rippling puddles on flat ground
    float puddle = 0.0, puddleDepth = 0.0, wetAmt = 0.0, shin = userShin;
    if (uWetness > 0.001)
    {
        vec3 ng = normalize(fs.normal);
        if (!gl_FrontFacing) ng = -ng;
        float flatness = smoothstep(0.85, 0.97, ng.y);
        float pn = puddleNoise(fs.worldPos.xz);
        float level = 0.62 - 0.3 * uWetness;                       // puddles spread as it keeps raining
        // The shoreline is exactly one pixel wide however close or far you are (fwidth), so it never looks soft
        float edge = fwidth(pn) * 1.1 + 0.0005;
        puddle = smoothstep(level - edge, level + edge, pn) * flatness * smoothstep(0.08, 0.5, uWetness);   // they shrink and vanish as the ground dries
        // How deep the water is: nothing at the shore, full in the middle of the puddle (a dip fills from the bottom)
        puddleDepth = smoothstep(level, level + 0.14, pn) * puddle;
        float rim = smoothstep(level - 0.16, level, pn) * (1.0 - puddle) * flatness;               // damp ground that fades out round each puddle
        wetAmt = max(uWetness * (1.0 - puddle), rim * uWetness);

        // Wet ground is darker; the ground under deeper water darker still (more water, more absorbed light)
        albedo *= (1.0 - 0.4 * wetAmt) * mix(1.0, mix(0.62, 0.38, puddleDepth), puddle);
        if (puddle > 0.0)
        {
            vec2 slope = rainRipples(fs.worldPos.xz, uWeatherTime, uRainAmount);
        n = normalize(mix(n, normalize(vec3(-slope.x * 1.6, 1.0, -slope.y * 1.6)), puddle));
        }
        shin = mix(userShin, 400.0, clamp(wetAmt * 0.8 + puddle, 0.0, 1.0));
        specColor = mix(specColor * (1.0 + 2.0 * wetAmt), vec3(1.5), puddle);
    }

    vec3 result = uAmbient * albedo;

    if (uHasDirLight)
    {
        vec3 l = normalize(-uDirLight.direction);
        float shadow = (uShadowsEnabled && uReceiveShadow && uCloudStorm < 0.97) ? shadowFactor(n, l) * (1.0 - uCloudStorm) : 0.0;   // no hard shadows under an overcast sky
        float sunSpec = pow(1.0 - uCloudStorm, 2.0);   // an overcast sky leaves no sun glints
        float clouds = uCloudShadows ? cloudShadowFactor(fs.worldPos, l) : 1.0;
        result += (1.0 - shadow) * clouds * blinnPhong(n, v, l, uDirLight.color, albedo, specColor, shin, sunSpec);
        result += (1.0 - shadow) * clouds * uDirLight.color * albedo * max(dot(n, l), 0.0) * causticLight(fs.worldPos);
    }

    for (int i = 0; i < uPointLightCount; ++i)
    {
        vec3 toLight = uPointLights[i].position - fs.worldPos;
        float dist = length(toLight);
        float att = rangeAttenuation(dist, uPointLights[i].range);
        result += att * blinnPhong(n, v, toLight / dist, uPointLights[i].color, albedo, specColor, shin, 1.0);
    }

    if (uHasSpot)
    {
        vec3 toLight = uSpot.position - fs.worldPos;
        float dist = length(toLight);
        vec3 l = toLight / dist;
        float theta = dot(l, normalize(-uSpot.direction));
        float cone = clamp((theta - uSpot.cosOuter) / max(uSpot.cosInner - uSpot.cosOuter, 1e-4), 0.0, 1.0);
        float att = rangeAttenuation(dist, uSpot.range) * cone;
        result += att * blinnPhong(n, v, l, uSpot.color, albedo, specColor, shin, 1.0);
    }

    if (uWetness > 0.001)
    {
        // Water films mirror the sky: bright fog colour at the horizon, darker overhead, so ripples are visible
        // Water reflects only about 2% looking straight down and nearly everything at a grazing angle (Schlick's Fresnel)
        float fresnel = 0.02 + 0.98 * pow(1.0 - max(dot(n, v), 0.0), 5.0);
        vec3 r = reflect(-v, n);
        vec3 skyRefl = mix(max(uFogColor, uAmbient * 2.0), uAmbient * 1.6, smoothstep(0.0, 0.7, r.y));
        if (uPuddleMirror)
        {
            // The real mirrored scene, bent by the ripples; only valid for puddles at the mirror height
            vec2 muv = gl_FragCoord.xy / uScreenSize + n.xz * 0.035;
            vec3 mirror = texture(uPuddleReflection, clamp(muv, vec2(0.002), vec2(0.998))).rgb;
            mirror = mix(mirror, uFogColor, 1.0 - exp(-uFogReflect * 60.0));   // the mirror image is not fogged
            float valid = 1.0 - smoothstep(0.04, 0.2, abs(fs.worldPos.y - uPuddleLevel));
            skyRefl = mix(skyRefl, mirror, valid);
        }
        // The thin film at the shore reflects less than the deep middle; the ground shows through it
        float film = mix(0.55, 1.0, smoothstep(0.0, 0.35, puddleDepth));
        result = mix(result, skyRefl, clamp(fresnel * puddle * film, 0.0, 1.0));
        result += skyRefl * fresnel * wetAmt * 0.25;
    }

    if (uFogDensity > 0.0)
    {
        // Height fog: thicker near the ground
        float dist = length(uViewPos - fs.worldPos);
        float density = uFogDensity * exp(-0.02 * max(fs.worldPos.y, 0.0));
        result = mix(result, uFogColor, 1.0 - exp(-dist * density));
    }

    result += userEmission;
    FragColor = vec4(result, base.a);
}
