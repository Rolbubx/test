layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aUV;
layout(location = 3) in vec3 aTangent;
layout(location = 4) in vec4 aColor;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;
uniform mat3 uNormalMatrix;
uniform mat4 uLightSpace;
uniform vec2 uUvScale;
uniform vec4 uClipPlane;   // keep geometry where dot(worldPos, xyz) + w >= 0 (reflection pass; GL_CLIP_DISTANCE0)

out VS_OUT
{
    vec3 worldPos;
    vec3 normal;
    vec3 tangent;
    vec2 uv;
    vec4 color;
    vec4 lightSpacePos;
} vs;

void main()
{
    vec4 world = uModel * vec4(aPosition, 1.0);
    vs.worldPos = world.xyz;
    vs.normal = normalize(uNormalMatrix * aNormal);
    vs.tangent = normalize(mat3(uModel) * aTangent);
    vs.uv = aUV * uUvScale;
    vs.color = aColor;
    vs.lightSpacePos = uLightSpace * world;
    gl_ClipDistance[0] = dot(world, uClipPlane);
    gl_Position = uProjection * uView * world;
}
