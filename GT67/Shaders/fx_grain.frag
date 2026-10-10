in vec2 vUV;
out vec4 FragColor;

uniform sampler2D uInput;
uniform float uTime;
uniform float uStrength;

float hash(vec2 p)
{
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

void main()
{
    vec3 color = texture(uInput, vUV).rgb;
    float lum = dot(color, vec3(0.2126, 0.7152, 0.0722));
    float noise = hash(gl_FragCoord.xy + fract(uTime) * 1000.0) - 0.5;
    // Strongest in the mid-tones, like film.
    color += noise * uStrength * (1.0 - abs(lum * 2.0 - 1.0) * 0.6);
    FragColor = vec4(color, 1.0);
}
