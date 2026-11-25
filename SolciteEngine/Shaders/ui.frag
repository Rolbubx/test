in vec2 vUV;
out vec4 FragColor;

uniform sampler2D uUi;   // RGBA, premultiplied alpha, row 0 = top of the screen

void main()
{
    FragColor = texture(uUi, vec2(vUV.x, 1.0 - vUV.y));
}
