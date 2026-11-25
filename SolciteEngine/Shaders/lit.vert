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
uniform float uVertexSnap; // screen shaders with 'vertexSnap N': vertices snap to a grid N rows high (0 = off)
uniform vec2 uScreenSize;

out VS_OUT
{
    vec3 worldPos;
    vec3 normal;
    vec3 tangent;
    vec2 uv;
    vec4 color;
    vec4 lightSpacePos;
    vec3 localPos;     // the model's own coordinates (for custom shaders)
    vec3 objectPos;    // the object's origin in the world
} vs;

// Custom shaders (.shader files with a 'vertex' block) put their function here
//@USER_VERTEX_DECLARATIONS@

void main()
{
    vec3 worldPos = (uModel * vec4(aPosition, 1.0)).xyz;
    vec3 worldNormal = normalize(uNormalMatrix * aNormal);
    vec2 uv = aUV * uUvScale;
    vec3 objectPos = (uModel * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
    //@USER_VERTEX_CALL@

    vec4 world = vec4(worldPos, 1.0);
    vs.worldPos = worldPos;
    vs.normal = normalize(worldNormal);
    vs.tangent = normalize(mat3(uModel) * aTangent);
    vs.uv = uv;
    vs.color = aColor;
    vs.lightSpacePos = uLightSpace * world;
    vs.localPos = aPosition;
    vs.objectPos = objectPos;
    gl_ClipDistance[0] = dot(world, uClipPlane);
    gl_Position = uProjection * uView * world;

    // PS1 look: the console had no sub-pixel precision, so vertices jumped from one coarse screen position to the next.
    if (uVertexSnap > 0.0 && gl_Position.w > 0.0)
    {
        vec2 grid = vec2(uVertexSnap * uScreenSize.x / max(uScreenSize.y, 1.0), uVertexSnap) * 0.5;
        gl_Position.xy = floor(gl_Position.xy / gl_Position.w * grid + 0.5) / grid * gl_Position.w;
    }
}
