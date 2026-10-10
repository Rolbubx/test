layout(location = 0) in vec3 aPosition;
layout(location = 2) in vec2 aUV;

out vec2 vUV;

void main()
{
    vUV = aUV;
    gl_Position = vec4(aPosition.xy, 0.0, 1.0); // the quad already spans -1..1
}
