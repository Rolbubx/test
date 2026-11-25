in vec2 vUV;
out vec4 FragColor;

uniform sampler2D uInput;
uniform vec2 uTexel;   // 1 / size of the (smaller) source texture
uniform float uRadius;

void main()
{
    // 3x3 tent filter, blended additively onto the larger mip
    vec2 d = uTexel * uRadius;
    vec3 a = texture(uInput, vUV + vec2(-d.x,  d.y)).rgb;
    vec3 b = texture(uInput, vUV + vec2( 0.0,  d.y)).rgb;
    vec3 c = texture(uInput, vUV + vec2( d.x,  d.y)).rgb;
    vec3 e = texture(uInput, vUV + vec2(-d.x,  0.0)).rgb;
    vec3 f = texture(uInput, vUV).rgb;
    vec3 g = texture(uInput, vUV + vec2( d.x,  0.0)).rgb;
    vec3 h = texture(uInput, vUV + vec2(-d.x, -d.y)).rgb;
    vec3 i = texture(uInput, vUV + vec2( 0.0, -d.y)).rgb;
    vec3 j = texture(uInput, vUV + vec2( d.x, -d.y)).rgb;

    vec3 result = f * 4.0 + (b + e + g + i) * 2.0 + (a + c + h + j);
    FragColor = vec4(result / 16.0, 1.0);
}
