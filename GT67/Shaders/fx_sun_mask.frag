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

float cloudDensity(vec3 p)
{
    float h = (p.y - uCloudMin) / (uCloudMax - uCloudMin);
    if (h <= 0.0 || h >= 1.0) return 0.0;

    float profile = smoothstep(0.0, 0.12, h) * smoothstep(1.0, 0.55, h);
    float overcast = uCloudStorm * profile * 0.75;   // storm: an unbroken sheet
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
