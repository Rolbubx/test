in vec2 vUV;
out vec4 FragColor;

uniform sampler2D uInput;
uniform sampler2D uDepth;
uniform sampler2D uShadowMap;
uniform bool uShadowsEnabled;
uniform mat4 uLightSpace;
uniform mat4 uInvViewProj;
uniform vec3 uCameraPos;
uniform vec3 uFront;
uniform float uNear;
uniform float uFar;
uniform float uTime;

uniform float uDensity;        // per metre at ground level
uniform float uHeightFalloff;  // per metre of height
uniform float uMaxDistance;    // marching stops here (the sky counts as this far away)
uniform int uSteps;
uniform vec3 uFogColor;        // ambient light scattered by the fog (linear)
uniform vec3 uSunDir;          // towards the sun
uniform vec3 uSunColor;        // linear, already dimmed by the storm
uniform float uAnisotropy;
uniform float uFrame;
uniform vec2 uWind;

// cloud cover blocks the sun for the whole fog volume (same cloud field as sky.frag)
uniform bool uCloudShadows;
uniform sampler3D uCloudShape;
uniform float uCloudMin;
uniform float uCloudMax;
uniform float uCloudStorm;
uniform float uCloudCoverage;
uniform float uCloudDensity;
uniform float uCloudShapeScale;
uniform vec3 uCloudOffset;

const float PI = 3.14159265;

float hash21(vec2 p)
{
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float valueNoise(vec2 p)
{
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash21(i), hash21(i + vec2(1.0, 0.0)), f.x),
               mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), f.x), f.y);
}

// Drifting banks of thicker and thinner fog
float fogNoise(vec3 p)
{
    vec2 drift = uWind * uTime;
    float a = valueNoise(p.xz * 0.035 + drift * 0.02 + p.y * 0.01);
    return 0.45 + 0.9 * a;
}

float linearDepth(float d)
{
    float z = d * 2.0 - 1.0;
    return 2.0 * uNear * uFar / (uFar + uNear - z * (uFar - uNear));
}

float cloudDensity(vec3 p)
{
    float h = (p.y - uCloudMin) / (uCloudMax - uCloudMin);
    if (h <= 0.0 || h >= 1.0) return 0.0;

    float profile = smoothstep(0.0, 0.12, h) * smoothstep(1.0, 0.55, h);
    float overcast = uCloudStorm * profile * 0.75;
    vec3 q = (p + uCloudOffset) * uCloudShapeScale;
    vec4 n = textureLod(uCloudShape, q, 2.5);
    float worleyFbm = n.g * 0.625 + n.b * 0.25 + n.a * 0.125;
    float baseShape = clamp((n.r - 0.35) / 0.65, 0.0, 1.0);
    float shape = baseShape * (0.4 + 0.6 * clamp(worleyFbm / 0.8, 0.0, 1.0));
    float weather = textureLod(uCloudShape, vec3(q.x * 0.1, 0.31, q.z * 0.1), 3.0).g;
    float coverage = clamp(uCloudCoverage + (weather - 0.45) * 0.7, 0.0, 1.0);
    if (coverage <= 0.001) return overcast * uCloudDensity;
    float density = (shape * profile - (1.0 - coverage)) / coverage;
    return clamp(max(density, overcast), 0.0, 1.0) * uCloudDensity;
}

// 0..1 of the sun that gets through the clouds at p
float cloudSunVisibility(vec3 p)
{
    if (!uCloudShadows || uSunDir.y < 0.02) return 1.0;
    float t0 = max((uCloudMin - p.y) / uSunDir.y, 0.0);
    float t1 = (uCloudMax - p.y) / uSunDir.y;
    if (t1 <= t0) return 1.0;
    float ds = (t1 - t0) / 4.0;
    float od = 0.0;
    for (int i = 0; i < 4; ++i)
        od += cloudDensity(p + uSunDir * (t0 + ds * (float(i) + 0.5))) * ds;
    return exp(-od * 0.006);
}

float shadowVisibility(vec3 p)
{
    if (!uShadowsEnabled) return 1.0;
    vec4 lp = uLightSpace * vec4(p, 1.0);
    vec3 s = lp.xyz / lp.w * 0.5 + 0.5;
    if (s.z > 1.0 || s.x < 0.0 || s.x > 1.0 || s.y < 0.0 || s.y > 1.0) return 1.0;
    return (s.z - 0.002 > texture(uShadowMap, s.xy).r) ? 0.0 : 1.0;
}

float henyeyGreenstein(float cosTheta, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * PI * pow(1.0 + g2 - 2.0 * g * cosTheta, 1.5));
}

void main()
{
    vec3 color = texture(uInput, vUV).rgb;
    float depth = texture(uDepth, vUV).r;

    vec4 far = uInvViewProj * vec4(vUV * 2.0 - 1.0, 1.0, 1.0);
    vec3 dir = normalize(far.xyz / far.w - uCameraPos);

    float tEnd = uMaxDistance;
    if (depth < 0.99999) tEnd = min(linearDepth(depth) / max(dot(dir, uFront), 1e-3), uMaxDistance);

    // Rays that climb out of the fog layer stop early: nothing left to march through
    if (dir.y > 0.0)
    {
        float topHeight = log(uDensity * 1e4 + 1.0) / uHeightFalloff;     // where the density has fallen to ~1e-4
        tEnd = min(tEnd, max(topHeight - uCameraPos.y, 0.0) / dir.y);
    }
    if (tEnd <= 0.01) { FragColor = vec4(color, 1.0); return; }

    // The cloud deck blocks the sun for the whole volume; the shadow map handles objects per sample
    float cloudVis = uCloudStorm > 0.97 ? 0.0 : cloudSunVisibility(uCameraPos + dir * tEnd * 0.5);
    bool sunOn = cloudVis > 0.01 && dot(uSunColor, vec3(1.0)) > 0.001;
    float cosTheta = dot(dir, uSunDir);
    float phase = 0.12 + 0.12 * henyeyGreenstein(cosTheta, uAnisotropy) * 4.0 * PI;

    float jitter = fract(52.9829189 * fract(dot(gl_FragCoord.xy + uFrame, vec2(0.06711056, 0.00583715))));
    float dt = tEnd / float(uSteps);

    float transmittance = 1.0;
    vec3 scattered = vec3(0.0);
    for (int i = 0; i < uSteps; ++i)
    {
        vec3 p = uCameraPos + dir * (dt * (float(i) + jitter));
        float d = uDensity * exp(-max(p.y, 0.0) * uHeightFalloff) * fogNoise(p);
        float sigma = d * dt;
        if (sigma < 1e-5) continue;

        vec3 light = uFogColor + uSunColor * phase * (sunOn ? cloudVis * shadowVisibility(p) : 0.0);
        float absorbed = 1.0 - exp(-sigma);
        scattered += transmittance * absorbed * light;
        transmittance *= 1.0 - absorbed;
        if (transmittance < 0.01) break;
    }

    FragColor = vec4(color * transmittance + scattered, 1.0);
}
