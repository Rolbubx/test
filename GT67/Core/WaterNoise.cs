namespace GT67.Core;

/// <summary>Tileable procedural normal map for water ripples (xyz = slope, alpha = height).</summary>
internal static class WaterNoise
{
    /// <summary>RGBA8, <c>size x size</c>. R/G hold the surface slope (0.5 = flat), A the height used for foam.</summary>
    public static byte[] NormalMap(int size, int seed)
    {
        // Height field: periodic Perlin (gradient) noise at doubling frequencies, its coordinates bent by a slow noise
        // (domain warping) so the ripples curl and bunch up like wind on water instead of sitting on a grid. The fine
        // octaves (down to a few texels) give the surface its crisp, glittering small ripples.
        var height = new float[size * size];
        int octaves = Math.Max(1, (int)MathF.Log2(size / 4f) - 1);   // 512 -> 6 octaves, 4 to 128 cells
        Parallel.For(0, size, y =>
        {
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                // Warp: shift the lookup by up to ~4% of the tile, with periodic noise so the result still tiles.
                float wu = u + 0.04f * Perlin(u, v, 3, seed + 101);
                float wv = v + 0.04f * Perlin(u + 0.37f, v + 0.71f, 3, seed + 202);
                float sum = 0f, amplitude = 1f;
                for (int octave = 0, cells = 4; octave < octaves; octave++, cells *= 2, amplitude *= 0.58f)
                    sum += amplitude * Perlin(wu, wv, cells, seed + octave * 17);
                height[y * size + x] = sum;
            }
        });

        // Normalise to 0..1 (Perlin is centred on 0).
        float min = height.Min(), max = height.Max();
        float range = MathF.Max(max - min, 1e-6f);
        for (int i = 0; i < height.Length; i++) height[i] = (height[i] - min) / range;

        var data = new byte[size * size * 4];
        const float slopeScale = 7f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float left = height[y * size + (x + size - 1) % size], right = height[y * size + (x + 1) % size];
            float down = height[((y + size - 1) % size) * size + x], up = height[((y + 1) % size) * size + x];
            float sx = (right - left) * slopeScale * size * 0.006f;
            float sy = (up - down) * slopeScale * size * 0.006f;

            int i = (y * size + x) * 4;
            data[i] = ToByte(-sx * 0.5f + 0.5f);
            data[i + 1] = ToByte(-sy * 0.5f + 0.5f);
            data[i + 2] = 255;
            data[i + 3] = ToByte(height[y * size + x]);
        }
        return data;
    }

    private static byte ToByte(float v) => (byte)(Math.Clamp(v, 0f, 1f) * 255f + 0.5f);

    /// <summary>
    /// Periodic 2D Perlin gradient noise, about -0.7..0.7: a random gradient at every lattice point (the lattice wraps
    /// every <paramref name="cells"/> cells, so it tiles over 0..1), blended with the quintic fade curve.
    /// </summary>
    private static float Perlin(float u, float v, int cells, int seed)
    {
        float px = u * cells, py = v * cells;
        int ix = (int)MathF.Floor(px), iy = (int)MathF.Floor(py);
        float fx = px - ix, fy = py - iy;
        float sx = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f), sy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);

        float Corner(int cx, int cy, float dx, float dy)
        {
            float angle = Random(cx, cy, cells, seed) * MathF.Tau;
            return MathF.Cos(angle) * dx + MathF.Sin(angle) * dy;
        }
        float a = Corner(ix, iy, fx, fy), b = Corner(ix + 1, iy, fx - 1f, fy);
        float c = Corner(ix, iy + 1, fx, fy - 1f), d = Corner(ix + 1, iy + 1, fx - 1f, fy - 1f);
        float top = a + (b - a) * sx, bottom = c + (d - c) * sx;
        return top + (bottom - top) * sy;
    }

    private static float Random(int x, int y, int cells, int seed)
    {
        x = ((x % cells) + cells) % cells;
        y = ((y % cells) + cells) % cells;
        unchecked
        {
            uint h = (uint)seed * 747796405u + 2891336453u;
            h ^= (uint)x * 374761393u;
            h = ((h << 13) | (h >> 19)) * 1274126177u;
            h ^= (uint)y * 668265263u;
            h = (h ^ (h >> 15)) * 2654435761u;
            return ((h ^ (h >> 16)) & 0xFFFFu) / 65535f;
        }
    }
}
