// Dissolve: the object burns away in noisy holes with a glowing edge (alpha cut-out).
// Animate 'Amount' from a script: obj.SetShaderParam("Amount", 0.7f)
param float Amount = 0.35 range 0 1
param float Scale = 3.0 range 0.5 20
param color EdgeColor = (1.0, 0.45, 0.1)
param float EdgeWidth = 0.06 range 0 0.3
param bool Animate = true

surface
{
    float amount = Animate ? 0.5 + 0.5 * sin(time * 0.6) : Amount;
    float n = fbm(localPosition * Scale, 5);            // follows the object when it moves
    float edge = 1.0 - smoothstep(0.0, EdgeWidth, n - amount);

    alpha = n - amount + 0.5;                           // below 0.5 (n < amount) the pixel is cut away
    albedo = base * (1.0 - edge * 0.8);
    emission = EdgeColor * edge * 4.0;
}
