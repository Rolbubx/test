in vec2 vUV;
out vec4 FragColor;

// Temporal accumulation of the shafts: this frame's noisy march is blended with last frame's result, found where the
// same point of the world was on screen then (reprojection). The noise offset changes every frame, so a few frames
// add up to many more samples per pixel. History that no longer matches (something moved in front, the view jumped)
// is clamped to what the pixels around show now, or dropped (variance clipping, Salvi 2016).

uniform sampler2D uCurrent;   // this frame's march (r, g = distance, b, a)
uniform sampler2D uHistory;   // last frame's accumulated result
uniform mat4 uInvViewProj;
uniform mat4 uPrevViewProj;
uniform vec3 uCameraPos;
uniform vec3 uPrevCameraPos;
uniform float uBlend;         // how much of the history is kept (0 = none, e.g. on the first frame)

void main()
{
    vec4 current = texture(uCurrent, vUV);

    // The world point this pixel sees, and where it was on screen last frame
    vec4 far = uInvViewProj * vec4(vUV * 2.0 - 1.0, 1.0, 1.0);
    vec3 dir = normalize(far.xyz / far.w - uCameraPos);
    vec3 p = uCameraPos + dir * current.g;
    vec4 clip = uPrevViewProj * vec4(p, 1.0);
    vec2 prevUV = clip.xy / clip.w * 0.5 + 0.5;
    bool onScreen = clip.w > 0.0 && all(greaterThanEqual(prevUV, vec2(0.0))) && all(lessThanEqual(prevUV, vec2(1.0)));
    if (uBlend <= 0.0 || !onScreen)
    {
        FragColor = current;
        return;
    }

    // What the 3x3 pixels around show now: the history is clipped to their mean +- 1.5 standard deviations
    vec2 texel = 1.0 / vec2(textureSize(uCurrent, 0));
    vec3 m1 = vec3(0.0), m2 = vec3(0.0);
    for (int y = -1; y <= 1; ++y)
        for (int x = -1; x <= 1; ++x)
        {
            vec3 s = texture(uCurrent, vUV + vec2(x, y) * texel).rba;
            m1 += s;
            m2 += s * s;
        }
    m1 /= 9.0;
    vec3 sigma = sqrt(max(m2 / 9.0 - m1 * m1, 0.0));
    vec3 lo = m1 - 1.5 * sigma, hi = m1 + 1.5 * sigma;

    vec4 history = texture(uHistory, prevUV);
    // The same surface? Its distance from last frame's camera must match what was stored then
    float expected = length(p - uPrevCameraPos);
    float same = exp(-abs(history.g - expected) / (expected * 0.04 + 0.3));

    vec3 kept = clamp(history.rba, lo, hi);
    vec3 result = mix(current.rba, kept, uBlend * same);
    FragColor = vec4(result.x, current.g, result.y, result.z);
}
