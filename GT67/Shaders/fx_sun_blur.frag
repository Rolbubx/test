in vec2 vUV;
out vec4 FragColor;

uniform sampler2D uInput;   // occlusion mask
uniform vec2 uSunPos;
uniform float uDensity;
uniform float uDecay;
uniform float uWeight;

const int SAMPLES = 96;

void main()
{
    // Radial blur towards the sun: samples along the line from this pixel to the sun. Each pixel starts at a slightly
    // different point along it, which breaks the stepped "rings" into fine grain the eye does not see.
    vec2 delta = (vUV - uSunPos) * uDensity / float(SAMPLES);
    float jitter = fract(52.9829189 * fract(dot(gl_FragCoord.xy, vec2(0.06711056, 0.00583715))));
    vec2 p = vUV + delta * jitter;
    float illumination = 1.0;
    vec3 sum = vec3(0.0);
    for (int i = 0; i < SAMPLES; ++i)
    {
        p -= delta;
        sum += texture(uInput, p).rgb * illumination * uWeight;
        illumination *= uDecay;
    }
    FragColor = vec4(clamp(sum, vec3(0.0), vec3(8.0)), 1.0);
}
