in vec2 vUV;
out vec4 FragColor;

// Volumetric sun shafts (half resolution): every pixel marches along its view ray through a thin haze of air and adds
// the sunlight scattered towards the camera wherever that air is lit. Objects (the shadow map) and clouds (the cloud
// field) leave shadowed lanes in it, so the shafts exist in the world: they are seen in every direction, brightest
// towards the sun, and keep their shape when the sun is behind the camera.
// Output: r = light scattered towards the camera (multiplied by the sun's colour later), g = distance (for the blur),
// b = the same if nothing cast a shadow (r / b is how lit the air on this ray is), a = how much haze the ray crosses.

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
uniform float uHaze;           // scattering per metre at ground level
uniform float uHeightFalloff;  // per metre of height
uniform float uMaxDistance;
uniform int uSteps;
uniform float uForward;        // forward scattering (Henyey-Greenstein g)
uniform float uSpread;         // share of light scattered evenly in every direction (shafts seen looking away)

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

void main()
{
    float depth = texture(uDepth, vUV).r;
    vec4 far = uInvViewProj * vec4(vUV * 2.0 - 1.0, 1.0, 1.0);
    vec3 dir = normalize(far.xyz / far.w - uCameraPos);

    float tEnd = uMaxDistance;
    if (depth < 0.99999) tEnd = min(linearDepth(depth) / max(dot(dir, uFront), 1e-3), uMaxDistance);

    // A different offset for every pixel and frame: the blur turns the noise into smooth shafts instead of bands
    float jitter = fract(52.9829189 * fract(dot(gl_FragCoord.xy + uFrame * 5.588238, vec2(0.06711056, 0.00583715))));

    // Samples crowd near the camera (t grows with the square), where shadows change fastest
    float n = float(uSteps);
    float transmittance = 1.0;
    float lit = 0.0, full = 0.0, haze = 0.0;
    for (int i = 0; i < uSteps; ++i)
    {
        float a0 = (float(i) + jitter) / n, a1 = (float(i) + 1.0 + jitter) / n;
        float t = tEnd * a0 * a0;
        float dt = tEnd * (a1 * a1 - a0 * a0);
        vec3 p = uCameraPos + dir * t;
        float density = uHaze * exp(-max(p.y, 0.0) * uHeightFalloff);
        float sigma = density * dt;
        float visibility = shadowVisibility(p) * cloudSunVisibility(p);
        lit += transmittance * sigma * visibility;
        full += transmittance * sigma;
        haze += sigma;
        transmittance *= exp(-sigma);
    }

    // Brightest towards the sun (forward scattering), still there looking away (the even share)
    float cosTheta = dot(dir, uSunDir);
    float phase = mix(henyeyGreenstein(cosTheta, uForward), 1.0 / (4.0 * PI), uSpread) * 4.0 * PI;

    FragColor = vec4(lit * phase, tEnd, full * phase, haze);
}
