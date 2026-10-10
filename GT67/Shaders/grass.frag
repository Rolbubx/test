struct DirLight { vec3 direction; vec3 color; };

in vec3 vWorldPos;
in vec3 vNormal;
in float vHeight;
in float vSeed;
in float vPatch;
in float vGust;
in float vFlower;

out vec4 FragColor;

uniform vec3 uViewPos;
uniform vec3 uAmbient;
uniform bool uHasDirLight;
uniform DirLight uDirLight;
uniform bool uShadowsEnabled;
uniform float uShadowBias;
uniform sampler2D uShadowMap;
uniform mat4 uLightSpace;
uniform float uCloudStorm;
uniform float uWetness;
uniform float uFogDensity;
uniform vec3 uFogColor;

uniform vec3 uGrassBase;   // linear colours
uniform vec3 uGrassTip;

// 0 lit .. 1 shadowed (a small 4-tap filter: blades are thin, a soft edge is enough)
float grassShadow(vec3 p)
{
    vec4 lp = uLightSpace * vec4(p, 1.0);
    vec3 c = lp.xyz / lp.w * 0.5 + 0.5;
    if (c.z > 1.0 || c.x < 0.0 || c.x > 1.0 || c.y < 0.0 || c.y > 1.0) return 0.0;
    vec2 texel = 1.0 / vec2(textureSize(uShadowMap, 0));
    float s = 0.0;
    for (int i = 0; i < 4; ++i)
    {
        vec2 o = vec2(float(i & 1), float(i >> 1)) - 0.5;
        s += (c.z - uShadowBias * 2.0 > texture(uShadowMap, c.xy + o * texel * 1.5).r) ? 1.0 : 0.0;
    }
    return s * 0.25;
}

void main()
{
    // Darker and greener at the root, lighter at the tip, each blade a slightly different shade; drier, yellower
    // patches and lush, deep green ones across the field.
    float shade = 0.82 + 0.36 * fract(vSeed * 37.13);
    vec3 albedo = mix(uGrassBase, uGrassTip, smoothstep(0.0, 1.0, vHeight)) * shade;
    vec3 dry = albedo * vec3(1.25, 1.05, 0.55);
    vec3 lush = albedo * vec3(0.8, 1.0, 0.85);
    albedo = mix(dry, lush, smoothstep(0.25, 0.75, vPatch));

    // Wildflowers: a coloured head at the top of the stem.
    if (vFlower > 0.0 && vHeight > 0.8)
    {
        vec3 petals = vFlower < 1.5 ? vec3(0.95, 0.93, 0.85) : vFlower < 2.5 ? vec3(0.95, 0.78, 0.15) : vec3(0.55, 0.35, 0.85);
        albedo = mix(albedo, petals, smoothstep(0.8, 0.9, vHeight));
    }
    albedo *= 1.0 - 0.3 * uWetness;                     // wet grass is darker
    float occlusion = mix(0.35, 1.0, vHeight);          // the bottom of a lawn gets little light

    vec3 n = normalize(vNormal);
    vec3 v = normalize(uViewPos - vWorldPos);
    vec3 result = uAmbient * albedo * occlusion;

    if (uHasDirLight)
    {
        vec3 l = normalize(-uDirLight.direction);
        float shadow = (uShadowsEnabled && uCloudStorm < 0.97) ? grassShadow(vWorldPos) * (1.0 - uCloudStorm) : 0.0;
        float diffuse = max(dot(n, l), 0.0) * 0.7 + 0.3;                         // wrapped: blades are thin
        // Light shining through the blades when looking towards the sun.
        float through = pow(max(dot(-v, l), 0.0), 4.0) * vHeight * 0.8;
        float glossy = pow(max(dot(n, normalize(l + v)), 0.0), 24.0) * 0.15 * vHeight;
        result += uDirLight.color * (1.0 - shadow) * (albedo * (diffuse * occlusion + through) + glossy);
    }

    // Wind waves: where a gust presses the blades over, their shiny sides catch the sky and the field brightens in
    // a band that rolls across it.
    result += (uAmbient * 1.5 + (uHasDirLight ? uDirLight.color * 0.12 : vec3(0.0))) * vGust * vHeight * 0.35;

    if (uFogDensity > 0.0)
    {
        float dist = length(uViewPos - vWorldPos);
        float density = uFogDensity * exp(-0.02 * max(vWorldPos.y, 0.0));
        result = mix(result, uFogColor, 1.0 - exp(-dist * density));
    }
    FragColor = vec4(result, 1.0);
}
