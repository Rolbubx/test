in vec2 vUV;
out vec4 FragColor;

uniform sampler2D uInput;
uniform float uStrength;

void main()
{
    // Red and blue are sampled slightly towards/away from the centre, growing with distance.
    vec2 offset = (vUV - 0.5) * uStrength;
    float r = texture(uInput, vUV + offset).r;
    float g = texture(uInput, vUV).g;
    float b = texture(uInput, vUV - offset).b;
    FragColor = vec4(r, g, b, 1.0);
}
