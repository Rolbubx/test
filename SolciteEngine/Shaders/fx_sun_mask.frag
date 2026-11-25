in vec2 vUV;
out vec4 FragColor;

uniform sampler2D uInput;
uniform sampler2D uDepth;
uniform vec2 uSunPos;       // sun position in UV space
uniform float uAspect;
uniform float uSunSize;     // disc radius in UV space
uniform float uDiscBrightness;

// clouds block the sun (same cloud field as sky.frag)
uniform bool uCloudShadows;
uniform sampler3D uCloudShape;
uniform float uCloudStorm;        // 0..1: a uniform storm sheet fills the gaps
uniform float uCloudMin;
uniform float uCloudMax;
uniform float uCloudCoverage;
uniform float uCloudDensity;
uniform float uCloudShapeScale;
uniform vec3 uCloudOffset;
uniform mat4 uInvViewProj;
uniform vec3 uCameraPos;

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

float cloudDensity(vec3 p)
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

    float density = (shape * profile - (1.0 - coverage)) / coverage;
    return clamp(max(density, overcast), 0.0, 1.0) * uCloudDensity;
}

// 1 = clear sky, towards 0 = opaque cloud along the view ray of this pixel
float cloudTransmittance()
{
    if (!uCloudShadows) return 1.0;
    if (uCloudStorm > 0.97) return 0.0;   // a solid sheet: no sun, no rays
    vec4 far = uInvViewProj * vec4(vUV * 2.0 - 1.0, 1.0, 1.0);
    vec3 dir = normalize(far.xyz / far.w - uCameraPos);
    if (dir.y < 0.004 || uCameraPos.y >= uCloudMax) return 1.0;

    float t0 = max((uCloudMin - uCameraPos.y) / dir.y, 0.0);
    float t1 = min((uCloudMax - uCameraPos.y) / dir.y, 50000.0);
    if (t1 <= t0) return 1.0;

    const int STEPS = 10;
    float ds = (t1 - t0) / float(STEPS);
    float od = 0.0;
    for (int i = 0; i < STEPS; ++i)
        od += cloudDensity(uCameraPos + dir * (t0 + ds * (float(i) + 0.5))) * ds;
    return exp(-od * 0.006);
}

void main()
{
    // Only the sky emits light; anything in front of it occludes the rays.
    float sky = step(0.99999, texture(uDepth, vUV).r);
    vec3 color = texture(uInput, vUV).rgb;
    float lum = dot(color, vec3(0.2126, 0.7152, 0.0722));

    vec2 d = (vUV - uSunPos) * vec2(uAspect, 1.0);
    float dist = length(d);
    float disc = smoothstep(uSunSize, 0.0, dist);
    float halo = smoothstep(0.55, 0.0, dist);

    // Clouds in front of the sun dim the disc and the glow; the gaps between them let shafts through.
    float open = sky > 0.0 ? cloudTransmittance() : 0.0;
    // The bright sky around a low sun can be many times brighter than the disc: cap it so the rays stay a soft glow
    vec3 emitted = vec3(disc * uDiscBrightness + halo * min(lum, 2.0) * 0.2) * open;
    FragColor = vec4(emitted, 1.0);
}
