in vec2 vUV;
out vec4 FragColor;

uniform sampler2D uInput;
uniform float uBrightness;
uniform float uContrast;
uniform float uSaturation;
uniform vec3 uTint;

void main()
{
    vec3 c = texture(uInput, vUV).rgb;
    c = (c - 0.5) * uContrast + 0.5 + uBrightness;
    float lum = dot(c, vec3(0.2126, 0.7152, 0.0722));
    c = mix(vec3(lum), c, uSaturation);
    c *= uTint;
    FragColor = vec4(clamp(c, 0.0, 1.0), 1.0);
}
