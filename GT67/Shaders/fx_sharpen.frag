in vec2 vUV;
out vec4 FragColor;

uniform sampler2D uInput;
uniform vec2 uTexel;
uniform float uStrength;
uniform float uClarity;

vec3 tap(vec2 offset) { return texture(uInput, vUV + offset * uTexel).rgb; }

void main()
{
    vec3 c = texture(uInput, vUV).rgb;

    // Unsharp mask on the 4 direct neighbours (fine detail) ...
    vec3 near = (tap(vec2(1, 0)) + tap(vec2(-1, 0)) + tap(vec2(0, 1)) + tap(vec2(0, -1))) * 0.25;
    vec3 detail = c - near;

    // ... and on a wider ring (clarity), which lifts the contrast of larger shapes.
    vec3 wide = (tap(vec2(3, 3)) + tap(vec2(-3, 3)) + tap(vec2(3, -3)) + tap(vec2(-3, -3)) +
                 tap(vec2(4, 0)) + tap(vec2(-4, 0)) + tap(vec2(0, 4)) + tap(vec2(0, -4))) * 0.125;
    vec3 local = c - wide;

    // Limit the boost so bright edges do not ring.
    vec3 boost = clamp(detail * uStrength + local * uClarity, vec3(-0.07), vec3(0.07));
    FragColor = vec4(clamp(c + boost, 0.0, 1.0), 1.0);
}
