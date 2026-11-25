// Sway: bends the top of the object in the wind (grass, plants, trees, flags). The bottom stays put.
param float Strength = 0.15 range 0 1
param float Speed = 1.5 range 0 6
param float Height = 2.0 range 0.1 20      // how tall the object is: at this height it sways fully
param vec2 Wind = (1.0, 0.3)

vertex
{
    float bend = clamp(localPosition.y / Height, 0.0, 1.0);
    bend *= bend;                                         // stiff at the root, loose at the tip
    float gust = noise(objectPosition.xz * 0.2 + time * 0.3);   // neighbours sway out of step
    float wave = sin(time * Speed + objectPosition.x * 0.7 + objectPosition.z * 0.5) * 0.6 + gust;
    position.xz += normalize(Wind) * wave * Strength * bend * Height;
}

surface
{
    // A little colour variation from object to object, so a field of copies does not look stamped.
    albedo = base * (0.85 + 0.3 * hash(objectPosition.xz));
}
