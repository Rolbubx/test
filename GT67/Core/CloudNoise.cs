namespace GT67.Core;

/// <summary>Tileable procedural 3D noise (Perlin-Worley and Worley) used to shape and erode clouds.</summary>
internal static class CloudNoise
{
    /// <summary>
    /// RGBA8: R = Perlin-Worley (the base cloud shape), G/B/A = Worley noise at 4, 8 and 16 cells
    /// (combined in the shader into a Worley fBm that carves the shape).
    /// </summary>
    public static byte[] Shape(int size, int seed)
    {
        var data = new byte[size * size * size * 4];
        Parallel.For(0, size, z =>
        {
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size, w = (z + 0.5f) / size;
                float worley4 = Worley(u, v, w, 4, seed);
                float perlin = PerlinFbm(u, v, w, 4, 3, seed + 1);
                float perlinWorley = worley4 + perlin * (1f - worley4); // remap(perlin, 0, 1, worley, 1)

                int i = ((z * size + y) * size + x) * 4;
                data[i] = ToByte(perlinWorley);
                data[i + 1] = ToByte(worley4);
                data[i + 2] = ToByte(Worley(u, v, w, 8, seed + 2));
                data[i + 3] = ToByte(Worley(u, v, w, 16, seed + 3));
            }
        });
        return data;
    }

    /// <summary>RGBA8: R/G/B = Worley noise at 2, 4 and 8 cells (fine detail that erodes the cloud edges).</summary>
    public static byte[] Detail(int size, int seed)
    {
        var data = new byte[size * size * size * 4];
        Parallel.For(0, size, z =>
        {
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size, w = (z + 0.5f) / size;
                int i = ((z * size + y) * size + x) * 4;
                data[i] = ToByte(Worley(u, v, w, 2, seed));
                data[i + 1] = ToByte(Worley(u, v, w, 4, seed + 1));
                data[i + 2] = ToByte(Worley(u, v, w, 8, seed + 2));
                data[i + 3] = 255;
            }
        });
        return data;
    }

    private static byte ToByte(float v) => (byte)(Math.Clamp(v, 0f, 1f) * 255f + 0.5f);

    /// <summary>Periodic cellular noise: 1 at a feature point, falling to 0 one cell away.</summary>
    private static float Worley(float u, float v, float w, int cells, int seed)
    {
        float px = u * cells, py = v * cells, pz = w * cells;
        int ix = (int)MathF.Floor(px), iy = (int)MathF.Floor(py), iz = (int)MathF.Floor(pz);

        float best = float.MaxValue;
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int cx = ix + dx, cy = iy + dy, cz = iz + dz;
            uint h = Hash(Mod(cx, cells), Mod(cy, cells), Mod(cz, cells), seed);
            float fx = cx + Rand(h, 0) - px;
            float fy = cy + Rand(h, 1) - py;
            float fz = cz + Rand(h, 2) - pz;
            best = MathF.Min(best, fx * fx + fy * fy + fz * fz);
        }
        return 1f - MathF.Min(MathF.Sqrt(best), 1f);
    }

    /// <summary>Periodic gradient-noise fBm remapped to 0..1.</summary>
    private static float PerlinFbm(float u, float v, float w, int baseCells, int octaves, int seed)
    {
        float sum = 0f, amplitude = 0.5f, total = 0f;
        int cells = baseCells;
        for (int i = 0; i < octaves; i++)
        {
            sum += Perlin(u, v, w, cells, seed + i * 31) * amplitude;
            total += amplitude;
            amplitude *= 0.5f;
            cells *= 2;
        }
        return Math.Clamp(sum / total * 0.5f + 0.5f, 0f, 1f);
    }

    /// <summary>Periodic 3D gradient noise, roughly -1..1.</summary>
    private static float Perlin(float u, float v, float w, int cells, int seed)
    {
        float px = u * cells, py = v * cells, pz = w * cells;
        int ix = (int)MathF.Floor(px), iy = (int)MathF.Floor(py), iz = (int)MathF.Floor(pz);
        float fx = px - ix, fy = py - iy, fz = pz - iz;
        float sx = Fade(fx), sy = Fade(fy), sz = Fade(fz);

        float Corner(int i, int j, int k) =>
            Gradient(ix + i, iy + j, iz + k, fx - i, fy - j, fz - k, cells, seed);

        float x00 = Lerp(Corner(0, 0, 0), Corner(1, 0, 0), sx);
        float x10 = Lerp(Corner(0, 1, 0), Corner(1, 1, 0), sx);
        float x01 = Lerp(Corner(0, 0, 1), Corner(1, 0, 1), sx);
        float x11 = Lerp(Corner(0, 1, 1), Corner(1, 1, 1), sx);
        return Lerp(Lerp(x00, x10, sy), Lerp(x01, x11, sy), sz) * 1.15f;
    }

    private static float Gradient(int cx, int cy, int cz, float dx, float dy, float dz, int cells, int seed)
    {
        uint h = Hash(Mod(cx, cells), Mod(cy, cells), Mod(cz, cells), seed);
        float gx = Rand(h, 0) * 2f - 1f, gy = Rand(h, 1) * 2f - 1f, gz = Rand(h, 2) * 2f - 1f;
        return gx * dx + gy * dy + gz * dz;
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    private static int Mod(int x, int m) => ((x % m) + m) % m;

    private static float Rand(uint hash, int channel) => ((hash >> (channel * 10)) & 1023u) / 1023f;

    private static uint Hash(int x, int y, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)seed * 747796405u + 2891336453u;
            h ^= (uint)x * 374761393u;
            h = ((h << 13) | (h >> 19)) * 1274126177u;
            h ^= (uint)y * 668265263u;
            h = ((h << 13) | (h >> 19)) * 1274126177u;
            h ^= (uint)z * 2246822519u;
            h = (h ^ (h >> 15)) * 2654435761u;
            return h ^ (h >> 16);
        }
    }
}
