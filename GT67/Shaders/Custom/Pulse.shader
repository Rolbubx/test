// RainbowGlow: un shader arc-en-ciel pour le moteur GT67
param float Speed = 1.5
param float ColorWave = 3.0
param float Glow = 2.0

surface
{
    float hue = time * Speed + position.y * ColorWave;
    
    float r = 0.5 + 0.5 * cos(hue);
    float g = 0.5 + 0.5 * cos(hue + 2.09);
    float b = 0.5 + 0.5 * cos(hue + 4.18);
    
    albedo = base * vec3(r, g, b);
    emission = vec3(r, g, b) * Glow;
}
