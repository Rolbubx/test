in vec3 vDirection;
out vec4 FragColor;

// cubemap / gradient sky
uniform bool uHasCubemap;
uniform samplerCube uCubemap;
uniform vec3 uTop;
uniform vec3 uHorizon;
uniform vec3 uBottom;

// physically based sky (lengths in kilometres)
uniform float uFogDensity;   // per metre
uniform float uStorm;          // 0..1: the whole sky turns into a dark overcast, sunset glow and sun included
uniform vec3 uFogColor;
uniform bool uAtmosphere;
uniform vec3 uSunDir;            // unit vector towards the sun
uniform float uSunIntensity;
uniform float uSunDiscBrightness;
uniform float uSunRadius;        // angular radius, radians
uniform float uCameraHeight;
uniform float uPlanetRadius;
uniform float uAtmosphereRadius;
uniform vec3 uBetaR;
uniform float uBetaM;
uniform float uHr;
uniform float uHm;
uniform float uMieG;
uniform vec3 uGroundAlbedo;
uniform bool uStars;

// volumetric clouds (world units: metres)
uniform bool uClouds;
uniform sampler3D uCloudShape;
uniform sampler3D uCloudDetail;
uniform vec3 uCameraPos;
uniform float uCloudStorm;        // 0..1: a uniform storm sheet fills the gaps
uniform float uCloudMin;
uniform float uCloudMax;
uniform float uCloudCoverage;
uniform float uCloudDensity;
uniform float uCloudShapeScale;     // 1 / metres per noise tile
uniform float uCloudDetailTiling;
uniform float uCloudDetailStrength;
uniform vec3 uCloudOffset;          // wind drift
uniform int uCloudSteps;
uniform int uCloudLightSteps;
uniform vec3 uCloudSunColor;
uniform vec3 uCloudAmbient;
uniform float uCloudAnisotropy;
uniform float uCloudMaxDistance;
uniform float uCloudHaze;

const int VIEW_STEPS = 16;
const int LIGHT_STEPS = 8;
const float PI = 3.14159265;

bool raySphere(vec3 origin, vec3 dir, float radius, out float t0, out float t1)
{
    float b = dot(origin, dir);
    float c = dot(origin, origin) - radius * radius;
    float disc = b * b - c;
    if (disc < 0.0) { t0 = 0.0; t1 = 0.0; return false; }
    float s = sqrt(disc);
    t0 = -b - s;
    t1 = -b + s;
    return true;
}

float hash31(vec3 p)
{
    p = fract(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return fract((p.x + p.y) * p.z);
}

vec3 starField(vec3 dir)
{
    vec3 scaled = dir * 500.0;
    vec3 cell = floor(scaled);
    float h = hash31(cell);
    float d = length(fract(scaled) - 0.5);
    float star = smoothstep(0.9985, 1.0, h) * smoothstep(0.5, 0.15, d);
    float temperature = hash31(cell + 17.0);
    vec3 tint = mix(vec3(1.0, 0.8, 0.65), vec3(0.7, 0.8, 1.0), temperature);
    return tint * star * (0.4 + 0.6 * hash31(cell + 5.0));
}

// ---- volumetric clouds ----------------------------------------------------------------------------
// A flat slab of 3D-noise density ray-marched with Beer-Lambert absorption, a dual-lobe Henyey-Greenstein
// phase function (silver lining), self-shadowing towards the sun and sky ambient on the shaded sides.

const float CLOUD_SIGMA = 0.006;   // extinction per metre at density 1

float remap(float v, float a, float b, float c, float d)
{
    return c + (v - a) / (b - a) * (d - c);
}

float henyeyGreenstein(float cosTheta, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * PI * pow(1.0 + g2 - 2.0 * g * cosTheta, 1.5));
}

float interleavedGradientNoise(vec2 p)
{
    return fract(52.9829189 * fract(dot(p, vec2(0.06711056, 0.00583715))));
}

// 0..1 cloud density at a world position (metres); lod picks the noise mip level
float cloudDensity(vec3 p, float lod, bool detail)
{
    float h = (p.y - uCloudMin) / (uCloudMax - uCloudMin);
    if (h <= 0.0 || h >= 1.0) return 0.0;

    // Rounded cumulus profile: flat-ish base, billowing top
    float profile = smoothstep(0.0, 0.12, h) * smoothstep(1.0, 0.55, h);
    float overcast = uCloudStorm * profile * 0.75;   // storm: an unbroken sheet

    vec3 q = (p + uCloudOffset) * uCloudShapeScale;
    vec4 n = textureLod(uCloudShape, q, lod);
    float worleyFbm = n.g * 0.625 + n.b * 0.25 + n.a * 0.125;
    // The Perlin-Worley channel only spans ~0.35..1, so stretch it to 0..1 and carve it with the Worley fBm.
    float baseShape = clamp((n.r - 0.35) / 0.65, 0.0, 1.0);
    float shape = baseShape * (0.4 + 0.6 * clamp(worleyFbm / 0.8, 0.0, 1.0));

    // Large-scale weather: patches of more and less cloud
    float weather = textureLod(uCloudShape, vec3(q.x * 0.1, 0.31, q.z * 0.1), 3.0).g;
    float coverage = clamp(uCloudCoverage + (weather - 0.45) * 0.7, 0.0, 1.0);
    if (coverage <= 0.001) return overcast * uCloudDensity;

    float density = shape * profile;
    density = remap(density, 1.0 - coverage, 1.0, 0.0, 1.0);
    if (density <= 0.0) return overcast * uCloudDensity;

    if (detail)
    {
        vec3 dn = textureLod(uCloudDetail, q * uCloudDetailTiling, max(lod - 2.0, 0.0)).rgb;
        float detailFbm = dn.r * 0.625 + dn.g * 0.25 + dn.b * 0.125;
        // Wispy at the top, billowy at the bottom
        float erosion = mix(detailFbm, 1.0 - detailFbm, clamp(h * 4.0, 0.0, 1.0)) * uCloudDetailStrength;
        density = remap(density, erosion, 1.0, 0.0, 1.0);
    }
    return clamp(max(density, overcast), 0.0, 1.0) * uCloudDensity;
}

// optical depth from p towards the sun
float cloudLightDepth(vec3 p)
{
    float od = 0.0;
    float len = 100.0;
    for (int i = 0; i < uCloudLightSteps; ++i)
    {
        p += uSunDir * len;
        od += cloudDensity(p, 2.5, false) * len;
        len *= 1.8;
    }
    return od * CLOUD_SIGMA;
}

// Returns the sky colour seen through the clouds along dir (skyColor = the clear sky in that direction).
vec3 applyClouds(vec3 skyColor, vec3 dir)
{
    if (!uClouds || dir.y < 0.004 || uCameraPos.y >= uCloudMax) return skyColor;

    float t0 = max((uCloudMin - uCameraPos.y) / dir.y, 0.0);
    float t1 = min((uCloudMax - uCameraPos.y) / dir.y, uCloudMaxDistance);
    if (t1 <= t0) return skyColor;

    float ds = (t1 - t0) / float(uCloudSteps);
    float t = t0 + ds * interleavedGradientNoise(gl_FragCoord.xy);
    float lod = clamp(log2(max(ds * uCloudShapeScale * 64.0, 1.0)), 0.0, 5.0);

    float cosTheta = dot(dir, uSunDir);
    float phase = 0.55 * henyeyGreenstein(cosTheta, uCloudAnisotropy)
                + 0.45 * henyeyGreenstein(cosTheta, -0.3);
    phase *= 4.0 * PI * 0.5;   // isotropic scattering would give 1.0 (scaled to keep clouds from glowing)

    vec3 scattered = vec3(0.0);
    float transmittance = 1.0;

    for (int i = 0; i < uCloudSteps; ++i)
    {
        vec3 p = uCameraPos + dir * t;
        float d = cloudDensity(p, lod, true);
        if (d > 0.002)
        {
            float lightOd = cloudLightDepth(p);
            // Beer's law plus a cheap multiple-scattering term so deep cloud is not pitch black
            float energy = max(exp(-lightOd), exp(-lightOd * 0.25) * 0.7);
            float heightFrac = (p.y - uCloudMin) / (uCloudMax - uCloudMin);

            vec3 light = uCloudSunColor * energy * phase + uCloudAmbient * mix(0.55, 1.0, heightFrac);
            float stepTransmittance = exp(-d * CLOUD_SIGMA * ds);
            scattered += transmittance * light * (1.0 - stepTransmittance);
            transmittance *= stepTransmittance;
            if (transmittance < 0.01) break;
        }
        t += ds;
    }

    // Distant clouds fade into the sky, and clouds thin out towards the horizon.
    float haze = 1.0 - exp(-(t0 + t1) * 0.5 * uCloudHaze);
    float horizon = smoothstep(0.004, 0.08, dir.y);
    float coverage = (1.0 - transmittance) * horizon;

    vec3 clouds = mix(scattered, skyColor * (1.0 - transmittance), haze) * horizon;
    return skyColor * (1.0 - coverage) + clouds;
}

// Single-scattering atmosphere (Rayleigh + Mie), after Nishita et al. 1993.
vec3 atmosphere(vec3 dir)
{
    vec3 origin = vec3(0.0, uPlanetRadius + uCameraHeight, 0.0);

    float t0, t1;
    if (!raySphere(origin, dir, uAtmosphereRadius, t0, t1)) return vec3(0.0);
    t0 = max(t0, 0.0);

    float p0, p1;
    bool hitsPlanet = raySphere(origin, dir, uPlanetRadius, p0, p1) && p0 > 0.0;
    float tMax = hitsPlanet ? p0 : t1;

    float mu = dot(dir, uSunDir);
    float g = uMieG;
    float phaseR = 3.0 / (16.0 * PI) * (1.0 + mu * mu);
    float phaseM = 3.0 / (8.0 * PI) * ((1.0 - g * g) * (1.0 + mu * mu))
                 / ((2.0 + g * g) * pow(1.0 + g * g - 2.0 * g * mu, 1.5));

    float ds = (tMax - t0) / float(VIEW_STEPS);
    float odR = 0.0, odM = 0.0;
    vec3 sumR = vec3(0.0), sumM = vec3(0.0);

    for (int i = 0; i < VIEW_STEPS; ++i)
    {
        vec3 pos = origin + dir * (t0 + ds * (float(i) + 0.5));
        float h = length(pos) - uPlanetRadius;
        float hr = exp(-h / uHr) * ds;
        float hm = exp(-h / uHm) * ds;
        odR += hr;
        odM += hm;

        float s0, s1;
        if (raySphere(pos, uSunDir, uPlanetRadius, s0, s1) && s0 > 0.0) continue; // planet shadow
        float l0, l1;
        raySphere(pos, uSunDir, uAtmosphereRadius, l0, l1);

        float lds = l1 / float(LIGHT_STEPS);
        float lodR = 0.0, lodM = 0.0;
        for (int j = 0; j < LIGHT_STEPS; ++j)
        {
            float lh = length(pos + uSunDir * (lds * (float(j) + 0.5))) - uPlanetRadius;
            lodR += exp(-lh / uHr) * lds;
            lodM += exp(-lh / uHm) * lds;
        }

        vec3 tau = uBetaR * (odR + lodR) + vec3(uBetaM * 1.1) * (odM + lodM);
        vec3 attenuation = exp(-tau);
        sumR += attenuation * hr;
        sumM += attenuation * hm;
    }

    vec3 color = (sumR * uBetaR * phaseR + sumM * uBetaM * phaseM) * uSunIntensity;
    vec3 transmittance = exp(-(uBetaR * odR + vec3(uBetaM * 1.1) * odM));

    if (hitsPlanet)
    {
        // Rough ground: albedo lit by the sun and sky, dimmed by the air in between.
        float daylight = smoothstep(-0.1, 0.3, uSunDir.y);
        color += uGroundAlbedo * transmittance * uSunIntensity * 0.07 * daylight;
    }
    else
    {
        float night = 1.0 - smoothstep(-0.15, 0.02, uSunDir.y);
        if (uStars && night > 0.0)
            color += starField(dir) * night * 6.0 * transmittance;

        // Sun disc, reddened and dimmed by the atmosphere along the view ray.
        float disc = smoothstep(cos(uSunRadius * 1.15), cos(uSunRadius * 0.85), mu);
        color += disc * uSunDiscBrightness * transmittance;
        color = applyClouds(color, dir);
    }
    return color;
}

// Storm: an overcast sky has no sun, no sunset glow; it is a dim grey that is lighter near the horizon
vec3 applyStorm(vec3 color, vec3 dir)
{
    if (uStorm <= 0.0) return color;
    float amount = smoothstep(0.0, 0.6, uStorm);
    vec3 overcast = uFogColor * mix(1.3, 0.7, sqrt(clamp(abs(dir.y), 0.0, 1.0)));
    return mix(color, overcast, amount);
}

// Distance fog swallows the horizon first and climbs the sky as it thickens
vec3 applyFog(vec3 color, vec3 dir)
{
    if (uFogDensity <= 0.0) return color;
    float amount = 1.0 - exp(-uFogDensity * 3500.0);
    float blend = amount * exp(-abs(dir.y) * mix(10.0, 2.0, amount));
    return mix(color, uFogColor, clamp(blend, 0.0, 1.0));
}

// A sea that reaches the horizon (Water.ReachHorizon): below the horizon the sky is drawn as distant sea.
uniform bool uOcean;
uniform vec3 uOceanColor;    // linear: the deep colour, lit
uniform float uOceanF0;      // reflectivity looking straight down

// The sky itself in one direction: atmosphere (with clouds and sun), cubemap or plain gradient.
vec3 baseSky(vec3 dir)
{
    if (uAtmosphere) return atmosphere(dir);
    if (uHasCubemap) return texture(uCubemap, dir).rgb;
    float t = dir.y;
    return t > 0.0 ? mix(uHorizon, uTop, pow(t, 0.6)) : mix(uHorizon, uBottom, pow(-t, 0.5));
}

void main()
{
    vec3 dir = normalize(vDirection);

    if (uAtmosphere && uStorm >= 0.6)   // fully overcast: skip the atmosphere and the clouds, the sky is a flat grey
    {
        FragColor = vec4(applyFog(applyStorm(vec3(0.0), dir), dir), 1.0);
        return;
    }

    vec3 color;
    if (uOcean && dir.y < 0.0)
    {
        // Distant sea: its own deep colour, turning into a mirror of the sky towards the horizon (Fresnel).
        float cosView = clamp(-dir.y, 0.0, 1.0);
        float fresnel = uOceanF0 + (1.0 - uOceanF0) * pow(1.0 - cosView, 5.0);
        color = mix(uOceanColor, baseSky(vec3(dir.x, -dir.y, dir.z)), fresnel);
    }
    else
    {
        color = baseSky(dir);
    }

    if (uAtmosphere) color = applyStorm(color, dir);
    FragColor = vec4(applyFog(color, dir), 1.0);
}
