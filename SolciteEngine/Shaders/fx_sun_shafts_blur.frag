in vec2 vUV;
out vec4 FragColor;

// Smooths the noisy shafts along one axis, but not across depth edges (g = distance), so the light does not bleed
// from the sky onto the objects in front of it. Run twice: horizontally, then vertically. r, b and a are smoothed.

uniform sampler2D uInput;
uniform vec2 uStep;   // one texel along the blur's axis

void main()
{
    vec4 centre = texture(uInput, vUV);
    vec3 sum = vec3(0.0);
    float total = 0.0;
    for (int i = -6; i <= 6; ++i)
    {
        vec4 s = texture(uInput, vUV + uStep * float(i));
        float spatial = exp(-float(i * i) / 18.0);
        float sameSurface = exp(-abs(s.g - centre.g) / max(centre.g * 0.12, 0.25));
        float w = spatial * sameSurface;
        sum += s.rba * w;
        total += w;
    }
    sum /= max(total, 1e-5);
    FragColor = vec4(sum.x, centre.g, sum.y, sum.z);
}
