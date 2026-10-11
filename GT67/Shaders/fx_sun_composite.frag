in vec2 vUV;
out vec4 FragColor;

// Adds the sun's light to the picture: the volumetric shafts in the air (seen everywhere) and the glare streaming from
// the sun's disc (only while it is on screen). What makes beams stand out is contrast between neighbouring lanes of
// air: where a view ray crosses more shadowed air than the rays around it (a cloud's or an object's shadow), what lies
// behind is darkened; where it crosses more lit air (a gap), it is brightened. The surroundings come from a small,
// blurred copy of the shafts (a high mipmap level).

uniform sampler2D uInput;    // the picture
uniform sampler2D uShafts;   // r = lit haze, b = the same without shadows, a = haze along the ray (half resolution, mipmapped)
uniform sampler2D uGlare;    // the radial glare around the sun (half resolution)
uniform vec3 uShaftColor;    // the sun's light times the shafts' strength
uniform vec3 uGlareColor;    // the sun's tint times the glare's strength and fade
uniform float uDarken;       // strength of the lane contrast

float shadowedFraction(vec4 s) { return s.b > 1e-6 ? 1.0 - clamp(s.r / s.b, 0.0, 1.0) : 0.0; }

void main()
{
    vec3 color = texture(uInput, vUV).rgb;
    vec4 shafts = textureLod(uShafts, vUV, 0.0);
    vec4 around = textureLod(uShafts, vUV, 4.5);

    float lanes = shadowedFraction(shafts) - shadowedFraction(around);   // > 0: a shadow lane, < 0: a beam
    float thickness = 1.0 - exp(-shafts.a * 2.5);
    color *= clamp(1.0 - uDarken * 5.0 * lanes * thickness, 0.3, 1.9);

    color += shafts.r * uShaftColor;
    color += texture(uGlare, vUV).rgb * uGlareColor;
    FragColor = vec4(color, 1.0);
}
