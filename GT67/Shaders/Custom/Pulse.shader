// Pulse: a glowing, pulsing surface. Save the file and the engine reloads it by itself.
// 'param' lines become values you can change per object in the editor's Inspector.
param color Tint = (1.0, 0.45, 0.15)
param float Speed = 2.0
param float Glow = 1.0

// Inside 'surface' you write GLSL. You can read:
//   uv, position (world), normal, view (towards the camera), time (seconds), base (the object's colour)
// and set:
//   albedo (colour), emission (glow, added on top), shininess, specular (a multiplier)
surface
{
    float pulse = 0.5 + 0.5 * sin(time * Speed + position.y * 2.0);
    albedo = base * Tint;
    emission = Tint * pulse * Glow;
}
