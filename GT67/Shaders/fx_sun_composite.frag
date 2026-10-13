in vec2 vUV;
out vec4 FragColor;

// Adds the sun's light to the picture: the volumetric shafts in the air (seen everywhere) and the glare streaming from
// the sun's disc (only while it is on screen). The shafts are at half resolution: each pixel takes the four half-size
// texels around it, weighted by how close their distance is to its own (a depth-aware upsample), so the light does
// not bleed over the edges of objects. What lies behind the ground haze is dimmed by it.

uniform sampler2D uInput;    // the picture
uniform sampler2D uDepth;    // full-resolution depth
uniform sampler2D uShafts;   // r = sunlight scattered, g = distance, b = thin air in clouds' shadow, a = transmittance
uniform sampler2D uGlare;    // the radial glare around the sun (half resolution)
uniform vec3 uShaftColor;    // the sun's light times the shafts' strength
uniform vec3 uAmbientColor;  // the sky's light times the shafts' strength
uniform vec3 uGlareColor;    // the sun's tint times the glare's strength and fade
uniform float uExtinction;   // 1: the ground haze dims what lies behind it; 0: off
uniform float uDarken;       // strength of the lane contrast of the big beams
uniform sampler2D uLanes;    // r = thin air in clouds' shadow on far pixels, g = how far the pixel is (mipmapped)
uniform vec3 uSunDir;        // towards the sun
uniform vec3 uBeamColor;     // the sunlight a fully lit big beam adds

uniform mat4 uInvViewProj;
uniform vec3 uCameraPos;
uniform vec3 uFront;
uniform float uNear;
uniform float uFar;
uniform float uMaxDistance;

float linearDepth(float d)
{
    float z = d * 2.0 - 1.0;
    return 2.0 * uNear * uFar / (uFar + uNear - z * (uFar - uNear));
}

vec3 viewDirection()
{
    vec4 far = uInvViewProj * vec4(vUV * 2.0 - 1.0, 1.0, 1.0);
    return normalize(far.xyz / far.w - uCameraPos);
}

vec4 upsampledShafts()
{
    // This pixel's distance along its view ray, measured like the half-resolution pass
    float depth = texture(uDepth, vUV).r;
    vec3 dir = viewDirection();
    float distance = uMaxDistance;
    if (depth < 0.99999) distance = min(linearDepth(depth) / max(dot(dir, uFront), 1e-3), uMaxDistance);

    ivec2 size = textureSize(uShafts, 0);
    vec2 pos = vUV * vec2(size) - 0.5;
    ivec2 base = ivec2(floor(pos));
    vec2 f = fract(pos);

    vec4 sum = vec4(0.0);
    float total = 0.0;
    vec4 nearest = vec4(0.0, 0.0, 0.0, 1.0);
    float nearestGap = 1e30;
    for (int i = 0; i < 4; ++i)
    {
        ivec2 o = ivec2(i & 1, i >> 1);
        vec4 s = texelFetch(uShafts, clamp(base + o, ivec2(0), size - 1), 0);
        float gap = abs(s.g - distance);
        float bilinear = (o.x == 1 ? f.x : 1.0 - f.x) * (o.y == 1 ? f.y : 1.0 - f.y);
        float w = bilinear * exp(-gap / (distance * 0.03 + 0.1)) + 1e-6 * bilinear;
        sum += s * w;
        total += w;
        if (gap < nearestGap) { nearestGap = gap; nearest = s; }
    }
    // None of the four lies at this pixel's distance (a thin object): take the closest one
    return total > 1e-4 ? sum / total : nearest;
}

void main()
{
    vec3 color = texture(uInput, vUV).rgb;
    vec4 shafts = upsampledShafts();

    // The big beams between the clouds, on the sky and distant land only: where a ray crosses more lit thin air than
    // the far rays around it (the average of the far pixels: a high mipmap level of uLanes), it is brighter. As in
    // real skies, a shadowed lane is just the plain sky (it is never darkened), and the beams are bright looking
    // towards the sun (crepuscular rays) and faint looking away from it (anticrepuscular rays).
    vec2 around = textureLod(uLanes, vUV, 4.5).rg;
    float far = smoothstep(300.0, 1000.0, shafts.g);
    float beam = around.g > 1e-3 ? max(around.r / around.g - shafts.b, 0.0) * far : 0.0;
    float towardsSun = 0.5 + 0.5 * dot(viewDirection(), uSunDir);
    beam *= mix(0.15, 1.0, towardsSun * towardsSun * towardsSun);
    color += uBeamColor * min(beam * uDarken * 4.0, 1.0);

    // The ground haze: dims what lies behind it, glows with the sky's light and with the sun where the sun reaches it
    color *= mix(1.0, shafts.a, uExtinction);
    color += shafts.r * uShaftColor + (1.0 - shafts.a) * uAmbientColor;
    color += texture(uGlare, vUV).rgb * uGlareColor;
    FragColor = vec4(color, 1.0);
}
