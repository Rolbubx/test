in vec2 vUV;
out vec4 FragColor;

uniform sampler2D uInput;
uniform sampler2D uDepth;
uniform vec2 uTexel;
uniform float uNear;
uniform float uFar;
uniform float uFocus;      // focus distance (world units, along the view direction)
uniform float uRange;      // distance from the focus plane where blur reaches its maximum
uniform float uMaxBlur;    // maximum blur radius in pixels

const int SAMPLES = 40;
const float GOLDEN_ANGLE = 2.39996323;

float linearDepth(float d)
{
    float z = d * 2.0 - 1.0;
    return 2.0 * uNear * uFar / (uFar + uNear - z * (uFar - uNear));
}

// 0 = sharp, 1 = maximum blur
float circleOfConfusion(float depth)
{
    return clamp(abs(linearDepth(depth) - uFocus) / uRange, 0.0, 1.0);
}

void main()
{
    float centerDepth = texture(uDepth, vUV).r;
    float centerCoc = circleOfConfusion(centerDepth);
    float radius = centerCoc * uMaxBlur;

    vec3 center = texture(uInput, vUV).rgb;
    if (radius < 0.5)
    {
        FragColor = vec4(center, 1.0);
        return;
    }

    vec3 sum = center;
    float weightSum = 1.0;
    for (int i = 0; i < SAMPLES; ++i)
    {
        float r = sqrt((float(i) + 0.5) / float(SAMPLES));
        float theta = float(i) * GOLDEN_ANGLE;
        vec2 offset = vec2(cos(theta), sin(theta)) * r * radius * uTexel;
        vec2 uv = vUV + offset;

        float sampleDepth = texture(uDepth, uv).r;
        float sampleCoc = circleOfConfusion(sampleDepth);
        vec3 color = texture(uInput, uv).rgb;

        // Sharp foreground objects must not smear over the blurred background behind them.
        float w = (sampleDepth < centerDepth) ? clamp(sampleCoc * 2.0, 0.0, 1.0) : 1.0;
        sum += color * w;
        weightSum += w;
    }
    FragColor = vec4(sum / weightSum, 1.0);
}
