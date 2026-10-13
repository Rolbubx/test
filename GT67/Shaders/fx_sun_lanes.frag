in vec2 vUV;
out vec4 FragColor;

// Keeps only the far pixels (sky, distant land) of the shafts' "air in a cloud's shadow" channel, for the composite's
// lane contrast: r = that value on far pixels, g = 1 on far pixels. Mipmapped, the ratio r / g is the average of the
// far pixels around, so the floor and nearby objects never brighten or darken the sky next to them, and back.

uniform sampler2D uShafts;   // b = thin air in clouds' shadow, g = distance

void main()
{
    vec4 s = texture(uShafts, vUV);
    float far = smoothstep(300.0, 1000.0, s.g);
    FragColor = vec4(s.b * far, far, 0.0, 1.0);
}
