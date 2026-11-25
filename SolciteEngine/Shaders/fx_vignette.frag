in vec2 vUV;
out vec4 FragColor;

uniform sampler2D uInput;
uniform float uAspect;
uniform float uIntensity;
uniform float uRadius;
uniform float uSoftness;
uniform float uRoundness;   // 0 = follows the screen shape, 1 = circular

void main()
{
    vec3 color = texture(uInput, vUV).rgb;
    vec2 p = (vUV - 0.5) * 2.0;
    p.x *= mix(1.0, uAspect, uRoundness);
    float vignette = 1.0 - smoothstep(uRadius - uSoftness, uRadius, length(p) * 0.7071);
    FragColor = vec4(color * mix(1.0, vignette, uIntensity), 1.0);
}
