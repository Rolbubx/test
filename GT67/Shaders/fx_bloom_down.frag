in vec2 vUV;
out vec4 FragColor;

uniform sampler2D uInput;
uniform vec2 uTexel;        // 1 / size of the source texture
uniform bool uPrefilter;    // first step only: keep just the bright parts
uniform float uThreshold;
uniform float uKnee;

vec3 prefilter(vec3 c)
{
    c = min(c, vec3(50.0)); // tame fireflies
    float brightness = max(c.r, max(c.g, c.b));
    float soft = clamp(brightness - uThreshold + uKnee, 0.0, 2.0 * uKnee);
    soft = soft * soft / (4.0 * uKnee + 1e-5);
    float contribution = max(soft, brightness - uThreshold) / max(brightness, 1e-5);
    return c * contribution;
}

vec3 fetch(vec2 offset)
{
    vec3 c = texture(uInput, vUV + offset * uTexel).rgb;
    return uPrefilter ? prefilter(c) : c;
}

void main()
{
    // 13-tap downsample filter (as used in Call of Duty: Advanced Warfare)
    vec3 a = fetch(vec2(-2, 2)), b = fetch(vec2(0, 2)), c = fetch(vec2(2, 2));
    vec3 d = fetch(vec2(-2, 0)), e = fetch(vec2(0, 0)), f = fetch(vec2(2, 0));
    vec3 g = fetch(vec2(-2, -2)), h = fetch(vec2(0, -2)), i = fetch(vec2(2, -2));
    vec3 j = fetch(vec2(-1, 1)), k = fetch(vec2(1, 1));
    vec3 l = fetch(vec2(-1, -1)), m = fetch(vec2(1, -1));

    vec3 result = e * 0.125
                + (a + c + g + i) * 0.03125
                + (b + d + f + h) * 0.0625
                + (j + k + l + m) * 0.125;
    FragColor = vec4(result, 1.0);
}
