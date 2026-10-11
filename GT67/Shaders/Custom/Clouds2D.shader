// Clouds2D: a flat layer of clouds painted over the sky. Every sky pixel follows its view ray up to a plane at
// Height metres and reads the clouds there, so they shrink towards the horizon, drift with the wind and stay put
// when the camera moves, like real ones. Cartoon gives hard edges and two flat tones.
param float Height = 1500.0 range 200 6000      // altitude of the cloud layer, in metres
param float Size = 2500.0 range 300 10000       // how big the clouds are, in metres
param float Coverage = 0.5 range 0 1            // 0 = clear sky, 1 = overcast
param float Softness = 0.08 range 0.01 0.3      // how fuzzy the edges are
param vec2 Wind = (25.0, 10.0)                  // drift, in metres per second (x, z)
param color Light = (1.0, 0.98, 0.95)           // the lit tops
param color Shadow = (0.6, 0.64, 0.74)          // the thick, darker undersides
param float Opacity = 0.9 range 0 1
param float HorizonFade = 0.12 range 0.01 0.5   // clouds fade out this close to the horizon
param bool Cartoon = false

screen
{
    if (!isSky(uv)) return;                      // only the sky: objects and ground stay as they are

    vec3 ray = normalize(worldPosition - cameraPosition);
    if (ray.y <= 0.0) return;                    // looking down: no clouds below the horizon

    // Where the ray meets the cloud layer, moved by the wind over time
    float distanceToLayer = (Height - cameraPosition.y) / ray.y;
    vec2 p = (cameraPosition.xz + ray.xz * distanceToLayer + Wind * time) / Size;

    // Two layers of noise: the big shapes, and finer detail that breaks up their edges
    float shape = fbm(p, 5);
    float detail = fbm(p * 4.0 + vec2(13.7, 3.1) + time * 0.01, 3);
    float n = shape * 0.8 + detail * 0.2;

    float threshold = 0.72 - Coverage * 0.4;
    float density, thickness;
    if (Cartoon)
    {
        density = step(threshold, n);
        thickness = step(threshold + 0.07, n) * 0.7;
    }
    else
    {
        density = smoothstep(threshold - Softness, threshold + Softness, n);
        thickness = saturate((n - threshold) * 3.0);
    }

    vec3 cloud = mix(Light, Shadow, thickness);
    float horizon = smoothstep(0.0, HorizonFade, ray.y);
    color = mix(color, cloud, density * Opacity * horizon);
}
