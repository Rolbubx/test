in vec2 vUV;
out vec4 FragColor;

uniform sampler2D uScene;
uniform bool uEnabled;
uniform float uExposure;
uniform float uGamma;
uniform int uToneMap; // 0 = Reinhard, 1 = ACES

vec3 aces(vec3 x)
{
    // Narkowicz ACES fit
    return clamp((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), 0.0, 1.0);
}

void main()
{
    vec3 hdr = texture(uScene, vUV).rgb;
    if (!uEnabled)
    {
        FragColor = vec4(clamp(hdr, 0.0, 1.0), 1.0);
        return;
    }

    vec3 mapped = hdr * uExposure;
    mapped = (uToneMap == 0) ? mapped / (mapped + vec3(1.0)) : aces(mapped);
    FragColor = vec4(pow(mapped, vec3(1.0 / uGamma)), 1.0);
}
