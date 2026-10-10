using System.Runtime.InteropServices;
using GT67.Utils;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>
/// The software mixer behind <see cref="Sound"/>: a background thread mixes every playing sound (volume, pitch,
/// 3D distance and panning) and streams the result to the sound card through winmm, so no extra audio package is
/// needed. Windows only: elsewhere sounds simply stay silent. Like <see cref="ModelCache"/>, this is intentionally static.
/// </summary>
internal static class AudioEngine
{
    private const int FramesPerBuffer = 1024;   // ~23 ms
    private const int BufferCount = 4;

    private static readonly object Gate = new();
    private static readonly List<Sound> Playing = new();
    private static Thread? _thread;
    private static volatile bool _run;
    private static bool _failed;
    private static volatile float _listenerX, _listenerY, _listenerZ, _rightX, _rightY, _rightZ = 1f;

    /// <summary>Master volume of everything, 0..2.</summary>
    public static float MasterVolume { get; set; } = 1f;

    public static bool Supported => OperatingSystem.IsWindows();

    /// <summary>Where the player hears from: the camera.</summary>
    public static void SetListener(Vector3 position, Vector3 right)
    {
        _listenerX = position.X; _listenerY = position.Y; _listenerZ = position.Z;
        _rightX = right.X; _rightY = right.Y; _rightZ = right.Z;
    }

    public static void Start(Sound sound)
    {
        lock (Gate)
        {
            if (!sound.Active) Playing.Add(sound);
            sound.IsPausedFlag = false;
            sound.Active = true;
        }
        EnsureThread();
    }

    public static void Stop(Sound sound)
    {
        lock (Gate)
        {
            Playing.Remove(sound);
            sound.Active = false;
            sound.IsPausedFlag = false;
            sound.Cursor = 0;
        }
    }

    public static void Shutdown()
    {
        _run = false;
        _thread?.Join(500);
        _thread = null;
        lock (Gate)
        {
            foreach (var sound in Playing) { sound.Active = false; sound.Cursor = 0; }
            Playing.Clear();
        }
    }

    private static void EnsureThread()
    {
        if (_run || _failed || !Supported) return;
        _run = true;
        _thread = new Thread(Run) { IsBackground = true, Name = "Audio mixer", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    // ---- mixing ----------------------------------------------------------------------------------

    private static void Mix(float[] accumulator, Sound[] sounds, int count)
    {
        Array.Clear(accumulator);
        float dt = FramesPerBuffer / (float)AudioClip.SampleRate;
        float master = MathF.Max(MasterVolume, 0f);

        for (int s = 0; s < count; s++)
        {
            var sound = sounds[s];
            if (!sound.Active || sound.IsPausedFlag) continue;
            var clip = sound.Clip;
            if (clip == null) { Stop(sound); continue; }

            // Fades
            if (sound.FadeGain != sound.FadeTarget)
            {
                float step = sound.FadeRate * dt;
                sound.FadeGain = MathF.Abs(sound.FadeTarget - sound.FadeGain) <= step
                    ? sound.FadeTarget
                    : sound.FadeGain + MathF.Sign(sound.FadeTarget - sound.FadeGain) * step;
                if (sound.FadeGain <= 0f && sound.StopAfterFade) { Stop(sound); continue; }
            }

            // 3D: distance fades the sound, the side it is on pans it
            float volume = MathF.Max(sound.Volume, 0f) * master * sound.FadeGain;
            float left = 1f, right = 1f;
            if (sound.Spatial)
            {
                var offset = new Vector3(sound.Position.X - _listenerX, sound.Position.Y - _listenerY, sound.Position.Z - _listenerZ);
                float distance = offset.Length;
                float min = MathF.Max(sound.MinDistance, 0.01f);
                float max = MathF.Max(sound.MaxDistance, min + 0.01f);
                volume *= min / MathF.Max(distance, min) * Math.Clamp((max - distance) / (max * 0.25f), 0f, 1f);

                float pan = distance > 1e-3f
                    ? (offset.X * _rightX + offset.Y * _rightY + offset.Z * _rightZ) / distance * Math.Clamp(distance / min, 0f, 1f)
                    : 0f;
                float angle = (Math.Clamp(pan, -1f, 1f) + 1f) * MathF.PI * 0.25f;
                left = MathF.Cos(angle) * 1.4142f;
                right = MathF.Sin(angle) * 1.4142f;
            }

            float targetLeft = volume * left, targetRight = volume * right;
            if (!sound.GainsReady)
            {
                sound.GainLeft = targetLeft;
                sound.GainRight = targetRight;
                sound.GainsReady = true;
            }

            var data = clip.Samples;
            int frames = clip.Frames;
            double cursor = sound.Cursor;
            double pitch = Math.Clamp(sound.Pitch, 0.1f, 8f);
            bool loop = sound.Loop;
            bool ended = false;

            for (int f = 0; f < FramesPerBuffer; f++)
            {
                int i = (int)cursor;
                if (i >= frames)
                {
                    if (!loop) { ended = true; break; }
                    cursor %= frames;
                    i = (int)cursor;
                }

                int next = i + 1 < frames ? i + 1 : (loop ? 0 : i);
                float t = (float)(cursor - i);
                float l = data[i * 2] + (data[next * 2] - data[i * 2]) * t;
                float r = data[i * 2 + 1] + (data[next * 2 + 1] - data[i * 2 + 1]) * t;

                float blend = (f + 1) / (float)FramesPerBuffer;
                float gl = sound.GainLeft + (targetLeft - sound.GainLeft) * blend;
                float gr = sound.GainRight + (targetRight - sound.GainRight) * blend;
                accumulator[f * 2] += l * gl;
                accumulator[f * 2 + 1] += r * gr;
                cursor += pitch;
            }

            sound.GainLeft = targetLeft;
            sound.GainRight = targetRight;
            sound.Cursor = cursor;
            if (ended) Stop(sound);
        }
    }

    /// <summary>Linear below 0.9, then a smooth squeeze towards 1 so loud mixes do not crackle.</summary>
    private static short ToPcm(float x)
    {
        float a = MathF.Abs(x);
        if (a > 0.9f) a = 0.9f + 0.1f * MathF.Tanh((a - 0.9f) * 10f);
        return (short)(MathF.Sign(x) * a * 32700f);
    }

    private static void Run()
    {
        if (!OperatingSystem.IsWindows()) return;

        var format = new WaveFormat
        {
            FormatTag = 1, Channels = 2, SamplesPerSec = AudioClip.SampleRate, BitsPerSample = 16,
            BlockAlign = 4, AvgBytesPerSec = AudioClip.SampleRate * 4, ExtraSize = 0,
        };

        if (WinMm.waveOutOpen(out var device, 0xFFFFFFFF, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0)
        {
            Log.Warn("Sound: no audio output device, sounds stay silent.");
            _failed = true;
            _run = false;
            return;
        }

        int dataBytes = FramesPerBuffer * 4;
        int headerSize = Marshal.SizeOf<WaveHeader>();
        var headers = new IntPtr[BufferCount];
        var data = new IntPtr[BufferCount];
        var accumulator = new float[FramesPerBuffer * 2];
        var pcm = new short[FramesPerBuffer * 2];
        var snapshot = new Sound[64];

        void Fill(int index)
        {
            int count;
            lock (Gate)
            {
                count = Math.Min(Playing.Count, snapshot.Length);
                for (int i = 0; i < count; i++) snapshot[i] = Playing[i];
            }

            Mix(accumulator, snapshot, count);
            for (int i = 0; i < pcm.Length; i++) pcm[i] = ToPcm(accumulator[i]);
            Marshal.Copy(pcm, 0, data[index], pcm.Length);
        }

        try
        {
            for (int i = 0; i < BufferCount; i++)
            {
                data[i] = Marshal.AllocHGlobal(dataBytes);
                headers[i] = Marshal.AllocHGlobal(headerSize);
                Marshal.StructureToPtr(new WaveHeader { Data = data[i], BufferLength = (uint)dataBytes }, headers[i], false);
                WinMm.waveOutPrepareHeader(device, headers[i], (uint)headerSize);
                Fill(i);
                WinMm.waveOutWrite(device, headers[i], (uint)headerSize);
            }

            int flagsOffset = (int)Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.Flags));
            while (_run)
            {
                bool wrote = false;
                for (int i = 0; i < BufferCount; i++)
                {
                    if ((Marshal.ReadInt32(headers[i], flagsOffset) & 1) == 0) continue;   // WHDR_DONE
                    Fill(i);
                    WinMm.waveOutWrite(device, headers[i], (uint)headerSize);
                    wrote = true;
                }
                if (!wrote) Thread.Sleep(4);
            }
        }
        finally
        {
            WinMm.waveOutReset(device);
            for (int i = 0; i < BufferCount; i++)
            {
                if (headers[i] != IntPtr.Zero)
                {
                    WinMm.waveOutUnprepareHeader(device, headers[i], (uint)headerSize);
                    Marshal.FreeHGlobal(headers[i]);
                }
                if (data[i] != IntPtr.Zero) Marshal.FreeHGlobal(data[i]);
            }
            WinMm.waveOutClose(device);
            _run = false;
        }
    }

    // ---- winmm -----------------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat
    {
        public ushort FormatTag, Channels;
        public uint SamplesPerSec, AvgBytesPerSec;
        public ushort BlockAlign, BitsPerSample, ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength, BytesRecorded;
        public IntPtr User;
        public uint Flags, Loops;
        public IntPtr Next, Reserved;
    }

    private static class WinMm
    {
        [DllImport("winmm.dll")] public static extern int waveOutOpen(out IntPtr device, uint id, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] public static extern int waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] public static extern int waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] public static extern int waveOutWrite(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] public static extern int waveOutReset(IntPtr device);
        [DllImport("winmm.dll")] public static extern int waveOutClose(IntPtr device);
    }
}
