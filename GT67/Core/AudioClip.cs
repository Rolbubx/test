using System.Text;
using GT67.Utils;

namespace GT67.Core;

/// <summary>
/// A decoded sound file: stereo float samples at the mixer's sample rate. Reads .wav (8/16/24/32-bit PCM and float),
/// .ogg (Vorbis) and .mp3. Loaded once per file and shared by every <see cref="Sound"/> that plays it.
/// </summary>
public sealed class AudioClip
{
    /// <summary>The mixer's sample rate; every clip is converted to it when it is loaded.</summary>
    public const int SampleRate = 44100;

    public static readonly string[] Extensions = { ".wav", ".ogg", ".mp3" };

    private static readonly Dictionary<string, AudioClip?> Cache = new(StringComparer.OrdinalIgnoreCase);

    private AudioClip(string path, float[] samples)
    {
        Path = path;
        Samples = samples;
    }

    public string Path { get; }

    /// <summary>Interleaved left/right samples, -1..1.</summary>
    public float[] Samples { get; }

    public int Frames => Samples.Length / 2;

    public float Duration => Frames / (float)SampleRate;

    public static bool IsAudioFile(string path) =>
        Extensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Loads (or fetches from the cache) the file at an asset path or absolute path. Null if it cannot be read.</summary>
    public static AudioClip? Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string full = ModelCache.ResolvePath(path);
        lock (Cache)
        {
            if (Cache.TryGetValue(full, out var cached)) return cached;
        }

        AudioClip? clip = null;
        try
        {
            if (!File.Exists(full)) throw new FileNotFoundException("File not found.", full);
            var (samples, channels, rate) = Decode(full);
            clip = new AudioClip(full, ToMixerFormat(samples, channels, rate));
            Log.Info($"Loaded sound '{path}' ({clip.Duration:0.0#} s).");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException
                                      or ArgumentException or FormatException)
        {
            Log.Warn($"Could not load sound '{path}': {e.Message}");
        }

        lock (Cache) Cache[full] = clip;
        return clip;
    }

    // ---- decoding --------------------------------------------------------------------------------

    private static (float[] samples, int channels, int rate) Decode(string file)
    {
        switch (System.IO.Path.GetExtension(file).ToLowerInvariant())
        {
            case ".wav": return DecodeWav(file);
            case ".ogg":
            {
                using var reader = new NVorbis.VorbisReader(file);
                return (ReadAll(reader.Channels, (b, o, c) => reader.ReadSamples(b, o, c)), reader.Channels, reader.SampleRate);
            }
            case ".mp3":
            {
                using var reader = new NLayer.MpegFile(file);
                return (ReadAll(reader.Channels, (b, o, c) => reader.ReadSamples(b, o, c)), reader.Channels, reader.SampleRate);
            }
            default:
                throw new NotSupportedException($"'{System.IO.Path.GetExtension(file)}' is not a supported sound format (use .wav, .ogg or .mp3).");
        }
    }

    private static float[] ReadAll(int channels, Func<float[], int, int, int> read)
    {
        var all = new List<float>();
        var buffer = new float[16384 * Math.Max(channels, 1)];
        int count;
        while ((count = read(buffer, 0, buffer.Length)) > 0)
            for (int i = 0; i < count; i++) all.Add(buffer[i]);
        return all.ToArray();
    }

    private static (float[] samples, int channels, int rate) DecodeWav(string file)
    {
        byte[] data = File.ReadAllBytes(file);
        if (data.Length < 44 || Encoding.ASCII.GetString(data, 0, 4) != "RIFF" || Encoding.ASCII.GetString(data, 8, 4) != "WAVE")
            throw new InvalidDataException("Not a WAV file.");

        int format = 0, channels = 0, rate = 0, bits = 0;
        int pcmStart = -1, pcmLength = 0;
        for (int pos = 12; pos + 8 <= data.Length;)
        {
            string id = Encoding.ASCII.GetString(data, pos, 4);
            int size = BitConverter.ToInt32(data, pos + 4);
            int body = pos + 8;
            if (id == "fmt ")
            {
                format = BitConverter.ToUInt16(data, body);
                channels = BitConverter.ToUInt16(data, body + 2);
                rate = BitConverter.ToInt32(data, body + 4);
                bits = BitConverter.ToUInt16(data, body + 14);
                if (format == 0xFFFE && size >= 26) format = BitConverter.ToUInt16(data, body + 24);   // extensible: the real format
            }
            else if (id == "data")
            {
                pcmStart = body;
                pcmLength = Math.Min(size < 0 ? int.MaxValue : size, data.Length - body);
                break;
            }
            pos = body + size + (size & 1);
        }

        if (pcmStart < 0 || channels < 1 || rate < 1) throw new InvalidDataException("The WAV file has no readable audio.");
        bool isFloat = format == 3;
        if (format != 1 && !isFloat) throw new NotSupportedException("Only PCM and float WAV files are supported (not compressed ones).");

        int bytes = bits / 8;
        int count = pcmLength / bytes;
        var samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            int at = pcmStart + i * bytes;
            samples[i] = bits switch
            {
                8 => (data[at] - 128) / 128f,
                16 => BitConverter.ToInt16(data, at) / 32768f,
                24 => (((data[at + 2] << 24) | (data[at + 1] << 16) | (data[at] << 8)) >> 8) / 8388608f,
                32 when isFloat => BitConverter.ToSingle(data, at),
                32 => BitConverter.ToInt32(data, at) / 2147483648f,
                _ => throw new NotSupportedException($"{bits}-bit WAV files are not supported."),
            };
        }
        return (samples, channels, rate);
    }

    /// <summary>Makes any channel count stereo and resamples to <see cref="SampleRate"/> (linear interpolation).</summary>
    private static float[] ToMixerFormat(float[] samples, int channels, int rate)
    {
        channels = Math.Max(channels, 1);
        int frames = samples.Length / channels;
        var stereo = new float[frames * 2];
        for (int f = 0; f < frames; f++)
        {
            float left, right;
            if (channels == 1)
            {
                left = right = samples[f];
            }
            else
            {
                left = samples[f * channels];
                right = samples[f * channels + 1];
            }
            stereo[f * 2] = left;
            stereo[f * 2 + 1] = right;
        }

        if (rate == SampleRate || frames < 2) return stereo;

        int outFrames = (int)((long)frames * SampleRate / rate);
        var result = new float[outFrames * 2];
        double step = (double)rate / SampleRate;
        for (int f = 0; f < outFrames; f++)
        {
            double at = f * step;
            int i = Math.Min((int)at, frames - 2);
            float t = (float)(at - i);
            result[f * 2] = stereo[i * 2] + (stereo[i * 2 + 2] - stereo[i * 2]) * t;
            result[f * 2 + 1] = stereo[i * 2 + 1] + (stereo[i * 2 + 3] - stereo[i * 2 + 1]) * t;
        }
        return result;
    }
}
