in vec2 vUV;
out vec4 FragColor;

uniform sampler2D uInput;
uniform float uExposure;      // EV stops
uniform float uTemperature;   // -1 cool .. +1 warm
uniform float uTint;          // -1 green .. +1 magenta
uniform float uHue;           // radians
uniform float uSaturation;
uniform float uVibrance;
uniform float uContrast;
uniform float uBrightness;
uniform vec3 uLift;
uniform vec3 uGamma;
uniform vec3 uGain;

const vec3 LUMA = vec3(0.2126, 0.7152, 0.0722);

vec3 hueShift(vec3 c, float angle)
{
    // Rotation around the grey axis (1,1,1)
    const vec3 k = vec3(0.57735026);
    float cosA = cos(angle);
    return c * cosA + cross(k, c) * sin(angle) + k * dot(k, c) * (1.0 - cosA);
}

void main()
{
    vec3 c = texture(uInput, vUV).rgb;

    // Exposure and white balance
    c *= exp2(uExposure);
    c *= vec3(1.0 + uTemperature * 0.25, 1.0 - uTint * 0.2, 1.0 - uTemperature * 0.25);

    // Lift / gamma / gain
    c = pow(max(c * uGain + uLift, 0.0), 1.0 / max(uGamma, vec3(0.01)));

    // Contrast around mid-grey, brightness
    c = (c - 0.5) * uContrast + 0.5 + uBrightness;

    // Hue, saturation, vibrance (vibrance boosts dull colours more than vivid ones)
    c = hueShift(c, uHue);
    float luma = dot(c, LUMA);
    float chroma = max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b));
    float saturation = uSaturation * (1.0 + uVibrance * (1.0 - clamp(chroma, 0.0, 1.0)));
    c = mix(vec3(luma), c, saturation);

    FragColor = vec4(clamp(c, 0.0, 1.0), 1.0);
}
