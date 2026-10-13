in vec2 vUV;
out vec4 FragColor;

// Volumetric sun shafts (half resolution): every pixel marches along its view ray and adds up the sunlight that the
// air scatters towards the camera wherever that air is lit. Two layers of air:
// - the ground haze around the player (dense, a few tens of metres): where the shadows of trees, walls and other
//   objects (the shadow map) cut the visible beams. It also dims what lies behind it and glows with the sky's light,
//   so its shadowed lanes are bluish air, never black;
// - the thin air up to the clouds (kilometres): the big beams between the clouds (the cloud field).
// The light is integrated in the energy-conserving way of Frostbite's volumetrics (Hillaire 2015): each step adds
// what its slab of air scatters, (1 - exp(-density * length)), seen through the air in front of it.
// Output: r = sunlight scattered towards the camera (times the sun's light later), g = distance (for the blur and
// the reprojection), b = how much of the thin air on this ray is in a cloud's shadow, times how thick that air is
// (the composite compares it with the rays around: the lanes of the big beams), a = how much of what lies behind
// gets through the ground haze (which glows with the sky's light by 1 - a).

uniform sampler2D uDepth;
uniform sampler2D uShadowMap;
uniform bool uShadowsEnabled;
uniform mat4 uLightSpace;
uniform mat4 uInvViewProj;
uniform vec3 uCameraPos;
uniform vec3 uFront;
uniform float uNear;
uniform float uFar;
uniform float uFrame;

uniform vec3 uSunDir;          // towards the sun
uniform float uForward;        // forward scattering (Henyey-Greenstein g)
uniform float uSpread;         // share of light scattered evenly in every direction (shafts seen looking away)

uniform float uGroundHaze;     // density of the ground haze per metre, at and below the camera's height
uniform float uGroundHeight;   // the ground haze thins by e every this many metres above the camera
uniform float uGroundRange;    // and fades out this far from the camera
uniform int uSteps;
uniform float uNoise;          // how patchy the ground haze is (0 = even)
uniform float uNoiseScale;     // 1 / size of the patches, per metre
uniform vec2 uWind;            // drift of the patches, metres per second (x, z) times uNoiseScale
uniform float uTime;

uniform float uHaze;           // the thin air: scattering per metre at height 0
uniform float uHeightFalloff;  // per metre of height
uniform float uMaxDistance;
uniform float uSkyShare;       // how bright the thin air's beams are next to the ground haze

// clouds cast the big shafts (same cloud field as sky.frag)
uniform bool uCloudShadows;
uniform bool uCloudShafts;
uniform sampler3D uCloudShape;
uniform float uCloudMin;
uniform float uCloudMax;
uniform float uCloudStorm;
uniform float uCloudCoverage;
uniform float uCloudDensity;
uniform float uCloudShapeScale;
uniform vec3 uCloudOffset;
uniform sampler2D uCloudWeather;
uniform float uCloudTime;
uniform float uCloudEvolution;
uniform float uCloudWeatherScale;
uniform float uCloudVariation;

const float PI = 3.14159265;
const int SKY_STEPS = 12;

float cloudWeather(vec2 xz)
{
    vec2 uv = xz * uCloudWeatherScale;
    float t = uCloudTime * uCloudEvolution * 0.0004;
    float a = texture(uCloudWeather, uv + vec2(t, t * 0.6)).a;
    float b = texture(uCloudWeather, uv * 1.73 + vec2(-t * 0.8, t * 1.1) + 0.37).a;
    return clamp(a * 0.65 + b * 0.35, 0.0, 1.0);
}

vec3 cloudEvolve() { return vec3(0.0, uCloudTime * uCloudEvolution * 0.0015, 0.0); }

float cloudDensity(vec3 p)
{
    float h = (p.y - uCloudMin) / (uCloudMax - uCloudMin);
    if (h <= 0.0 || h >= 1.0) return 0.0;
    float weather = cloudWeather(p.xz + uCloudOffset.xz);
    float top = mix(1.0, 0.35 + 0.65 * weather, uCloudVariation);
    float profile = smoothstep(0.0, 0.12, h) * (1.0 - smoothstep(top * 0.55, top, h));
    float overcast = uCloudStorm * profile * 0.75;
    vec3 q = (p + uCloudOffset) * uCloudShapeScale;
    vec4 n = textureLod(uCloudShape, q + cloudEvolve(), 1.5);   // finer than the sky's shadows: thin gaps cut thin beams
    float worleyFbm = n.g * 0.625 + n.b * 0.25 + n.a * 0.125;
    float baseShape = clamp((n.r - 0.35) / 0.65, 0.0, 1.0);
    float shape = baseShape * (0.4 + 0.6 * clamp(worleyFbm / 0.8, 0.0, 1.0));
    float coverage = clamp(uCloudCoverage + (weather - 0.5) * 1.4 * uCloudVariation, 0.0, 1.0);
    if (coverage <= 0.001) return overcast * uCloudDensity;
    float density = (shape * profile - (1.0 - coverage)) / coverage;
    return clamp(max(density, overcast), 0.0, 1.0) * uCloudDensity;
}

// How much sun gets through the clouds to p: three samples through the cloud deck along the sun's direction
float cloudSunVisibility(vec3 p)
{
    if (!uCloudShadows || !uCloudShafts || uSunDir.y < 0.02) return 1.0;
    float t0 = max((uCloudMin - p.y) / uSunDir.y, 0.0);
    float t1 = (uCloudMax - p.y) / uSunDir.y;
    if (t1 <= t0) return 1.0;
    float ds = (t1 - t0) / 3.0;
    float od = 0.0;
    for (int i = 0; i < 3; ++i)
        od += cloudDensity(p + uSunDir * (t0 + ds * (float(i) + 0.5))) * ds;
    return exp(-od * 0.006);
}

// 1 where the sun reaches p, 0 in an object's shadow (outside the shadow map: lit)
float shadowVisibility(vec3 p)
{
    if (!uShadowsEnabled) return 1.0;
    vec4 lp = uLightSpace * vec4(p, 1.0);
    vec3 s = lp.xyz / lp.w * 0.5 + 0.5;
    if (s.z > 1.0 || s.x < 0.0 || s.x > 1.0 || s.y < 0.0 || s.y > 1.0) return 1.0;
    // Fade out towards the edge of the shadow map, so the shafts of nearby objects do not end in a hard line
    vec2 edge = min(s.xy, 1.0 - s.xy);
    float inside = smoothstep(0.0, 0.08, min(edge.x, edge.y));
    float lit = (s.z - 0.002 > texture(uShadowMap, s.xy).r) ? 0.0 : 1.0;
    return mix(1.0, lit, inside);
}

float henyeyGreenstein(float cosTheta, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * PI * pow(max(1.0 + g2 - 2.0 * g * cosTheta, 1e-4), 1.5));
}

float linearDepth(float d)
{
    float z = d * 2.0 - 1.0;
    return 2.0 * uNear * uFar / (uFar + uNear - z * (uFar - uNear));
}

// How far the view ray goes: to the surface it hits, or for the sky as far as the shafts are traced. Below the horizon
// with nothing drawn there, the ray stops at the ground (height 0, or a little under the camera if it is lower):
// it must not march through kilometres of haze under the world, which drew streaks below the horizon.
float viewDistance(float depth, vec3 dir)
{
    if (depth < 0.99999) return min(linearDepth(depth) / max(dot(dir, uFront), 1e-3), uMaxDistance);
    float ground = min(0.0, uCameraPos.y - 10.0);
    if (dir.y < -1e-4) return min(uMaxDistance, (uCameraPos.y - ground) / -dir.y);
    return uMaxDistance;
}

// Smooth 3D value noise, for the haze drifting in the wind (denser and thinner patches, as in the volumetric fog of
// Assassin's Creed IV: procedural noise animated by the wind)
float hash(vec3 p)
{
    p = fract(p * 0.3183099 + 0.1);
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float valueNoise(vec3 p)
{
    vec3 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
               mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

float hazeNoise(vec3 p)
{
    vec3 q = p * uNoiseScale + vec3(uWind.x, 0.0, uWind.y) * uTime;
    return valueNoise(q) * 0.65 + valueNoise(q * 2.7 + 13.1) * 0.35;
}

void main()
{
    float depth = texture(uDepth, vUV).r;
    vec4 far = uInvViewProj * vec4(vUV * 2.0 - 1.0, 1.0, 1.0);
    vec3 dir = normalize(far.xyz / far.w - uCameraPos);

    float tEnd = viewDistance(depth, dir);

    // Interleaved gradient noise (Jimenez 2014), moved every frame: a different start for every pixel and frame.
    // The reprojection and the blur average the offsets into smooth beams instead of steps.
    float jitter = fract(52.9829189 * fract(dot(gl_FragCoord.xy + uFrame * 5.588238, vec2(0.06711056, 0.00583715))));

    // ---- the ground haze: samples crowd near the camera (t grows with the square), where shadows change fastest
    float tGround = min(tEnd, uGroundRange);
    float n = float(uSteps);
    float transmittance = 1.0, sunGround = 0.0;
    if (uGroundHaze > 0.0)
    {
        for (int i = 0; i < uSteps; ++i)
        {
            float a0 = (float(i) + jitter) / n, a1 = min((float(i) + 1.0 + jitter) / n, 1.0);
            float t = tGround * a0 * a0;
            float dt = tGround * (a1 * a1 - a0 * a0);
            vec3 p = uCameraPos + dir * t;
            float above = max(p.y - uCameraPos.y, 0.0);
            float density = uGroundHaze * exp(-above / uGroundHeight) * (1.0 - smoothstep(uGroundRange * 0.55, uGroundRange, t));
            if (uNoise > 0.0) density *= max(1.0 - uNoise + uNoise * 2.0 * hazeNoise(p), 0.0);
            float slab = 1.0 - exp(-density * dt);   // the share of light this slab scatters (energy-conserving)
            sunGround += transmittance * slab * shadowVisibility(p) * cloudSunVisibility(p);
            transmittance *= 1.0 - slab;
        }
    }
    // ---- the thin air up to the clouds: the big beams between the clouds, kilometres long
    float sunSky = 0.0, fullSky = 0.0, hazeSky = 0.0;
    if (uHaze > 0.0 && uSkyShare > 0.0)
    {
        float skyT = 1.0;
        for (int i = 0; i < SKY_STEPS; ++i)
        {
            float a0 = (float(i) + jitter) / float(SKY_STEPS), a1 = min((float(i) + 1.0 + jitter) / float(SKY_STEPS), 1.0);
            float t = tEnd * a0 * a0;
            float dt = tEnd * (a1 * a1 - a0 * a0);
            vec3 p = uCameraPos + dir * t;
            float slab = 1.0 - exp(-uHaze * exp(-max(p.y, 0.0) * uHeightFalloff) * dt);
            sunSky += skyT * slab * shadowVisibility(p) * cloudSunVisibility(p);
            fullSky += skyT * slab;
            hazeSky += slab;
            skyT *= 1.0 - slab;
        }
    }

    // Brightest towards the sun (forward scattering), still there looking away (the even share)
    float cosTheta = dot(dir, uSunDir);
    // Times pi: the engine lights a white surface with the full sun (no 1/pi), so evenly scattered haze gets a quarter of it
    float phase = mix(henyeyGreenstein(cosTheta, uForward), 1.0 / (4.0 * PI), uSpread) * PI;

    // The share of the thin air in shadow on this ray, times how thick that air is: the composite compares it with the
    // rays around (darker lanes in the shadows of clouds, bright beams through the gaps)
    float lanes = fullSky > 1e-6 ? (1.0 - clamp(sunSky / fullSky, 0.0, 1.0)) * (1.0 - exp(-hazeSky * 2.5)) : 0.0;
    FragColor = vec4((sunGround + sunSky * uSkyShare) * phase, tEnd, lanes, transmittance);
}
