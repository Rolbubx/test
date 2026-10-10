using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>
/// FFT ocean waves (Tessendorf, "Simulating Ocean Water"): a square patch of sea whose surface is the sum of
/// 128 x 128 = 16384 waves drawn at random from an ocean spectrum (Phillips, with the energy spread around the wind
/// direction), each moving at the speed real water waves of its length move. Every frame the spectrum is advanced in
/// time and turned into a height field, slopes and sideways (choppy) displacement with inverse FFTs, on all cores.
/// The patch tiles seamlessly; <see cref="Water"/> samples it twice at different scales and angles so the repetition
/// does not show. Foam is where the choppy displacement folds the surface (the Jacobian drops below 1).
/// </summary>
internal sealed class OceanWaves : IDisposable
{
    public const int N = 128;
    private const int Log2N = 7;
    private const float Gravity = 9.81f;

    // Initial spectrum h0(k), conj(h0(-k)), angular frequency and wave vector per frequency.
    private readonly float[] _h0Re = new float[N * N], _h0Im = new float[N * N];
    private readonly float[] _h0cRe = new float[N * N], _h0cIm = new float[N * N];
    private readonly float[] _omega = new float[N * N], _kx = new float[N * N], _kz = new float[N * N];

    // Five complex fields transformed every frame: height, slope x, slope z, displacement x, displacement z.
    private readonly float[][] _re = Enumerable.Range(0, 5).Select(_ => new float[N * N]).ToArray();
    private readonly float[][] _im = Enumerable.Range(0, 5).Select(_ => new float[N * N]).ToArray();

    // Results, double-buffered: a background task fills the back set while the front set is uploaded and read.
    private float[] _height = new float[N * N], _heightBack = new float[N * N];                    // for GetHeight (CPU)
    private float[] _displacement = new float[N * N * 4], _displacementBack = new float[N * N * 4]; // height, dx, dz, foam
    private float[] _slope = new float[N * N * 2], _slopeBack = new float[N * N * 2];               // dh/dx, dh/dz
    private Task? _work;

    private static readonly int[] BitReverse = Enumerable.Range(0, N).Select(i => Reverse(i)).ToArray();
    private static readonly float[] TwiddleRe = new float[N / 2], TwiddleIm = new float[N / 2];

    private (float patch, float windLength, Vector2 wind, float spread) _spectrumKey = (-1f, 0f, Vector2.Zero, 0f);
    private float _unitScale = 1f;     // brings the height field to an RMS of 0.5 per metre of WaveHeight
    private float _lastTime = float.NaN;
    private bool _disposed;

    static OceanWaves()
    {
        for (int i = 0; i < N / 2; i++)
        {
            float a = MathF.Tau * i / N;   // inverse transform: e^{+i...}
            TwiddleRe[i] = MathF.Cos(a);
            TwiddleIm[i] = MathF.Sin(a);
        }
    }

    public OceanWaves()
    {
        DisplacementTexture = CreateTexture(PixelInternalFormat.Rgba16f);
        SlopeTexture = CreateTexture(PixelInternalFormat.Rg16f);
    }

    /// <summary>GL texture (RGBA16F, repeat, mipmapped): height, displacement x, displacement z, foam 0..1.</summary>
    public int DisplacementTexture { get; }

    /// <summary>GL texture (RG16F, repeat, mipmapped): slope of the height field along x and z.</summary>
    public int SlopeTexture { get; }

    /// <summary>Metres covered by the patch (the textures repeat every PatchSize metres).</summary>
    public float PatchSize { get; private set; } = 40f;

    /// <summary>
    /// Brings the waves towards time <paramref name="time"/> (rebuilding the spectrum if the settings changed). The
    /// FFTs run on a background task: each call uploads the result the previous task finished (one frame behind, which
    /// does not show) and starts the next one, so the GL thread only pays for the upload. Must run on the GL thread.
    /// </summary>
    public void Update(float time, float waveLength, float waveHeight, float choppiness, Vector2 windDirection, float spread)
    {
        // The dominant wavelength of the Phillips spectrum is about 8.9 x its wind length L = V^2 / g.
        float patch = MathF.Max(waveLength * 8f, 8f);
        var wind = windDirection.LengthSquared > 1e-6f ? windDirection.Normalized() : Vector2.UnitX;
        var key = (patch, MathF.Max(waveLength, 0.05f) / 8.9f, wind, Math.Clamp(spread, 0f, 90f));
        bool rebuilt = false;
        if (key != _spectrumKey)
        {
            _work?.Wait();   // the task reads the spectrum: let it finish first
            _work = null;
            _spectrumKey = key;
            PatchSize = patch;
            BuildSpectrum(patch, key.Item2, wind, key.Item4);
            _lastTime = float.NaN;
            rebuilt = true;
        }
        if (time == _lastTime) return;

        // The first time (and after a rebuild) there is nothing to show yet: compute this frame right away.
        if (_work == null && (rebuilt || !_hasResult)) StartWork(time, waveHeight, choppiness).Wait();
        if (_work is { IsCompleted: true })
        {
            _work.Wait();   // surfaces an exception from the task, if any
            (_height, _heightBack) = (_heightBack, _height);
            (_displacement, _displacementBack) = (_displacementBack, _displacement);
            (_slope, _slopeBack) = (_slopeBack, _slope);
            _hasResult = true;
            Upload();
            _work = null;
        }
        if (_work == null)
        {
            _lastTime = time;
            StartWork(time, waveHeight, choppiness);
        }
    }

    private bool _hasResult;

    private Task StartWork(float time, float waveHeight, float choppiness)
    {
        float scale = waveHeight * _unitScale, chop = Math.Clamp(choppiness, 0f, 1f) * 1.3f;
        _work = Task.Run(() =>
        {
            Evaluate(time);
            Parallel.For(0, 5, f => InverseFft2D(_re[f], _im[f]));
            WriteTextures(scale, chop);
        });
        return _work;
    }

    /// <summary>Height in metres (WaveHeight already applied) of the last update at world (x, z), bilinear, tiling.</summary>
    public float GetHeight(float x, float z)
    {
        float texel = PatchSize / N;
        float fx = x / texel, fz = z / texel;
        int x0 = (int)MathF.Floor(fx), z0 = (int)MathF.Floor(fz);
        float tx = fx - x0, tz = fz - z0;
        float H(int ix, int iz) => _height[(((iz % N) + N) % N) * N + (((ix % N) + N) % N)];
        return (H(x0, z0) * (1 - tx) + H(x0 + 1, z0) * tx) * (1 - tz) + (H(x0, z0 + 1) * (1 - tx) + H(x0 + 1, z0 + 1) * tx) * tz;
    }

    // ---- spectrum ----------------------------------------------------------------------------------

    private void BuildSpectrum(float patch, float windLength, Vector2 wind, float spread)
    {
        // How tightly the waves follow the wind: spread 0 = cos^12, 90 = almost no preference.
        float exponent = 1f + 11f * (1f - spread / 90f);
        // Waves shorter than about two texels cannot be represented (they alias into noise): damp them away.
        float damping = patch / N * 0.7f;
        var random = new Random(20240607);
        for (int m = 0; m < N; m++)
        for (int n = 0; n < N; n++)
        {
            int i = m * N + n;
            float kx = MathF.Tau * (n - N / 2) / patch, kz = MathF.Tau * (m - N / 2) / patch;
            float k = MathF.Sqrt(kx * kx + kz * kz);
            _kx[i] = kx;
            _kz[i] = kz;
            _omega[i] = MathF.Sqrt(Gravity * k);

            float p = 0f;
            if (k > 1e-6f)
            {
                float cos = (kx * wind.X + kz * wind.Y) / k;
                float directional = MathF.Pow(MathF.Abs(cos), exponent) * (cos < 0f ? 0.25f : 1f);   // little against the wind
                float kl = k * windLength;
                p = MathF.Exp(-1f / (kl * kl)) / (k * k * k * k) * directional * MathF.Exp(-k * k * damping * damping);
            }
            float amplitude = MathF.Sqrt(p * 0.5f);
            _h0Re[i] = Gaussian(random) * amplitude;
            _h0Im[i] = Gaussian(random) * amplitude;
        }
        for (int m = 0; m < N; m++)
        for (int n = 0; n < N; n++)
        {
            int i = m * N + n, j = ((N - m) % N) * N + (N - n) % N;   // the index of -k
            _h0cRe[i] = _h0Re[j];
            _h0cIm[i] = -_h0Im[j];
        }

        // Normalise: one evaluation at t = 0 gives the RMS height of this spectrum.
        Evaluate(0f);
        InverseFft2D(_re[0], _im[0]);
        double sum = 0;
        for (int i = 0; i < N * N; i++) sum += _re[0][i] * (double)_re[0][i];
        float rms = (float)Math.Sqrt(sum / (N * N));
        _unitScale = rms > 1e-12f ? 0.5f / rms : 0f;
    }

    /// <summary>h(k, t) = h0(k) e^{iwt} + conj(h0(-k)) e^{-iwt}, and the slope and displacement fields derived from it.</summary>
    private void Evaluate(float time)
    {
        Parallel.For(0, N, m =>
        {
            for (int n = 0; n < N; n++)
            {
                int i = m * N + n;
                float c = MathF.Cos(_omega[i] * time), s = MathF.Sin(_omega[i] * time);
                float hr = _h0Re[i] * c - _h0Im[i] * s + _h0cRe[i] * c + _h0cIm[i] * s;
                float hi = _h0Re[i] * s + _h0Im[i] * c - _h0cRe[i] * s + _h0cIm[i] * c;
                float kx = _kx[i], kz = _kz[i];
                float k = MathF.Sqrt(kx * kx + kz * kz);
                float ux = k > 1e-6f ? kx / k : 0f, uz = k > 1e-6f ? kz / k : 0f;

                _re[0][i] = hr;        _im[0][i] = hi;          // height
                _re[1][i] = -kx * hi;  _im[1][i] = kx * hr;     // slope x: i kx h
                _re[2][i] = -kz * hi;  _im[2][i] = kz * hr;     // slope z
                _re[3][i] = ux * hi;   _im[3][i] = -ux * hr;    // displacement x: -i kx/k h
                _re[4][i] = uz * hi;   _im[4][i] = -uz * hr;    // displacement z
            }
        });
    }

    private void WriteTextures(float scale, float chop)
    {
        Parallel.For(0, N, z =>
        {
            for (int x = 0; x < N; x++)
            {
                int i = z * N + x;
                float sign = ((x + z) & 1) == 0 ? scale : -scale;   // undoes the centred frequency layout
                float h = _re[0][i] * sign;
                _heightBack[i] = h;
                _displacementBack[i * 4] = h;
                _displacementBack[i * 4 + 1] = _re[3][i] * sign * chop;
                _displacementBack[i * 4 + 2] = _re[4][i] * sign * chop;
                _slopeBack[i * 2] = _re[1][i] * sign;
                _slopeBack[i * 2 + 1] = _re[2][i] * sign;
            }
        });

        // Foam: the Jacobian of the choppy displacement. Below ~0.6 the surface is about to fold into a breaking crest.
        float texel = PatchSize / N;
        var d = _displacementBack;
        Parallel.For(0, N, z =>
        {
            for (int x = 0; x < N; x++)
            {
                int i = z * N + x;
                int right = z * N + (x + 1) % N, left = z * N + (x + N - 1) % N;
                int up = ((z + 1) % N) * N + x, down = ((z + N - 1) % N) * N + x;
                float dxdx = (d[right * 4 + 1] - d[left * 4 + 1]) / (2f * texel);
                float dzdz = (d[up * 4 + 2] - d[down * 4 + 2]) / (2f * texel);
                float dxdz = (d[up * 4 + 1] - d[down * 4 + 1]) / (2f * texel);
                float jacobian = (1f + dxdx) * (1f + dzdz) - dxdz * dxdz;
                d[i * 4 + 3] = Math.Clamp((0.75f - jacobian) * 2.5f, 0f, 1f);
            }
        });
    }

    // ---- FFT ---------------------------------------------------------------------------------------

    /// <summary>In-place inverse 2D FFT (rows, then columns), unnormalised.</summary>
    private static void InverseFft2D(float[] re, float[] im)
    {
        Parallel.For(0, N, row => Fft(re, im, row * N, 1));
        Parallel.For(0, N, column => Fft(re, im, column, N));
    }

    /// <summary>Radix-2 inverse FFT of N values starting at <paramref name="start"/>, every <paramref name="stride"/>.</summary>
    private static void Fft(float[] re, float[] im, int start, int stride)
    {
        Span<float> r = stackalloc float[N], m = stackalloc float[N];
        for (int i = 0; i < N; i++)
        {
            int j = start + BitReverse[i] * stride;
            r[i] = re[j];
            m[i] = im[j];
        }
        for (int size = 2, step = N / 2; size <= N; size *= 2, step /= 2)
        {
            int half = size / 2;
            for (int block = 0; block < N; block += size)
            for (int k = 0; k < half; k++)
            {
                float wr = TwiddleRe[k * step], wi = TwiddleIm[k * step];
                int a = block + k, b = a + half;
                float tr = r[b] * wr - m[b] * wi, ti = r[b] * wi + m[b] * wr;
                r[b] = r[a] - tr;
                m[b] = m[a] - ti;
                r[a] += tr;
                m[a] += ti;
            }
        }
        for (int i = 0; i < N; i++)
        {
            int j = start + i * stride;
            re[j] = r[i];
            im[j] = m[i];
        }
    }

    private static int Reverse(int value)
    {
        int result = 0;
        for (int bit = 0; bit < Log2N; bit++) result |= ((value >> bit) & 1) << (Log2N - 1 - bit);
        return result;
    }

    private static float Gaussian(Random random)
    {
        double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }

    // ---- GL ----------------------------------------------------------------------------------------

    /// <summary>
    /// The texture unit used for creating and uploading (never one the shaders sample from): binding on whatever unit
    /// happens to be active would silently replace a texture the next draw expects there.
    /// </summary>
    private const TextureUnit WorkUnit = TextureUnit.Texture15;

    private static int CreateTexture(PixelInternalFormat format)
    {
        int handle = GL.GenTexture();
        GL.ActiveTexture(WorkUnit);
        GL.BindTexture(TextureTarget.Texture2D, handle);
        var pixelFormat = format == PixelInternalFormat.Rg16f ? PixelFormat.Rg : PixelFormat.Rgba;
        GL.TexImage2D(TextureTarget.Texture2D, 0, format, N, N, 0, pixelFormat, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.GetFloat((GetPName)0x84FF, out float maxAnisotropy);   // GL_MAX_TEXTURE_MAX_ANISOTROPY (0 if unsupported)
        if (maxAnisotropy > 1f) GL.TexParameter(TextureTarget.Texture2D, (TextureParameterName)0x84FE, MathF.Min(8f, maxAnisotropy));
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        return handle;
    }

    private void Upload()
    {
        GL.ActiveTexture(WorkUnit);
        GL.BindTexture(TextureTarget.Texture2D, DisplacementTexture);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, N, N, PixelFormat.Rgba, PixelType.Float, _displacement);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        GL.BindTexture(TextureTarget.Texture2D, SlopeTexture);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, N, N, PixelFormat.Rg, PixelType.Float, _slope);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GL.DeleteTexture(DisplacementTexture);
        GL.DeleteTexture(SlopeTexture);
    }
}
