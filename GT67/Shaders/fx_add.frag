in vec2 vUV;
out vec4 FragColor;

uniform sampler2D uInput;   // base image
uniform sampler2D uOther;   // image added on top
uniform float uIntensity;
uniform vec3 uTint;

void main()
{
    vec3 base = texture(uInput, vUV).rgb;
    vec3 other = texture(uOther, vUV).rgb;
    FragColor = vec4(base + other * uTint * uIntensity, 1.0);
}
