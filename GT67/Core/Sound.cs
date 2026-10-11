using GT67.Utils;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>
/// A sound you can play from code or place in the editor (<c>Sound sound;</c>): a .wav, .ogg or .mp3 file with a
/// volume, pitch, looping and, optionally, a place in the world that makes it louder when you are close and pans it
/// left or right. Add it to the scene (<c>scene.Add(sound)</c>) so it is saved with the map, and start it with
/// <see cref="Play()"/>; <see cref="PlayOnStart"/> starts it when the game starts.
/// </summary>
/// <example>
/// <code>
/// var rainSound = new Sound("Assets/Sounds/rain.ogg") { Loop = true, Volume = 0.6f, PlayOnStart = true };
/// scene.Add(rainSound);
/// // a 3D sound that follows an object
/// scene.Add(new Sound("Assets/Sounds/engine.wav") { Loop = true, Spatial = true, Follow = car, PlayOnStart = true });
/// // a one-off
/// Sound.PlayOnce("Assets/Sounds/click.wav");
/// </code>
/// </example>
public sealed class Sound : IDisposable
{
    private string _file = "";
    private AudioClip? _clip;
    private bool _clipTried;
    private float _px, _py, _pz;
    private bool _autoStarted;

    // Mixer state (touched by the audio thread; see AudioEngine)
    internal double Cursor;                 // position in the clip, in frames
    internal volatile bool Active;          // being mixed (playing or paused)
    internal volatile bool IsPausedFlag;
    internal float GainLeft, GainRight;     // smoothed gains from the previous buffer
    internal bool GainsReady;
    internal float FadeGain = 1f, FadeTarget = 1f, FadeRate;
    internal bool StopAfterFade;
    internal bool Transient;                // a one-shot made by PlayOnce: removed when it ends

    public Sound() { }

    public Sound(string file) => File = file;

    public string Name { get; set; } = "Sound";

    /// <summary>Asset path ("Assets/Sounds/rain.ogg") or absolute path of a .wav, .ogg or .mp3 file.</summary>
    public string File
    {
        get => _file;
        set
        {
            if (_file == value) return;
            bool wasPlaying = IsPlaying;
            if (Active) Stop();
            _file = value ?? "";
            _clip = null;
            _clipTried = false;
            if (wasPlaying) Play();
        }
    }

    /// <summary>A disabled sound is silent and cannot be started.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Loudness, 0 (silent) to 1 (as recorded); above 1 boosts.</summary>
    public float Volume { get; set; } = 1f;

    /// <summary>Speed and pitch, 1 = normal, 2 = twice as fast and an octave higher.</summary>
    public float Pitch { get; set; } = 1f;

    /// <summary>Starts over when it reaches the end.</summary>
    public bool Loop { get; set; }

    /// <summary>Starts by itself when the game starts (in the editor: when you press Play), and stops when it ends.</summary>
    public bool PlayOnStart { get; set; }

    /// <summary>True: the sound comes from <see cref="Position"/> (or <see cref="Follow"/>): it fades with distance and pans.</summary>
    public bool Spatial { get; set; }

    /// <summary>Where a spatial sound is in the world.</summary>
    public Vector3 Position
    {
        get => new(_px, _py, _pz);
        set { _px = value.X; _py = value.Y; _pz = value.Z; }
    }

    /// <summary>Inside this distance a spatial sound is at full volume.</summary>
    public float MinDistance { get; set; } = 2f;

    /// <summary>Beyond this distance a spatial sound can no longer be heard.</summary>
    public float MaxDistance { get; set; } = 40f;

    /// <summary>A spatial sound that sticks to this object (its position is copied every frame).</summary>
    public GameObject? Follow { get; set; }

    // ---- state -----------------------------------------------------------------------------------

    public bool IsPlaying => Active && !IsPausedFlag;
    public bool IsPaused => Active && IsPausedFlag;

    /// <summary>Length of the file in seconds (0 if it could not be loaded).</summary>
    public float Length => Clip?.Duration ?? 0f;

    /// <summary>How far into the file the sound is, in seconds.</summary>
    public float Time => (float)(Cursor / AudioClip.SampleRate);

    internal AudioClip? Clip
    {
        get
        {
            if (_clip == null && !_clipTried)
            {
                _clipTried = true;
                _clip = AudioClip.Load(_file);
            }
            return _clip;
        }
    }

    // ---- control ---------------------------------------------------------------------------------

    /// <summary>Starts from the beginning (or continues if it was paused).</summary>
    public void Play()
    {
        if (!Enabled) return;
        if (Active && IsPausedFlag) { Resume(); return; }
        if (Clip == null) return;
        Cursor = 0;
        FadeGain = FadeTarget = 1f;
        FadeRate = 0f;
        StopAfterFade = false;
        GainsReady = false;
        AudioEngine.Start(this);
    }

    /// <summary>Starts from the beginning and fades in over <paramref name="fadeInSeconds"/>.</summary>
    public void Play(float fadeInSeconds)
    {
        Play();
        if (Active && fadeInSeconds > 0f)
        {
            FadeGain = 0f;
            FadeTo(1f, fadeInSeconds);
        }
    }

    public void Pause()
    {
        if (Active) IsPausedFlag = true;
    }

    public void Resume() => IsPausedFlag = false;

    public void Stop()
    {
        if (!Active) return;
        AudioEngine.Stop(this);
    }

    /// <summary>Fades the loudness multiplier to <paramref name="target"/> (0..1) over a number of seconds; stops the sound at 0 when asked.</summary>
    public void FadeTo(float target, float seconds, bool stopWhenSilent = false)
    {
        FadeTarget = Math.Clamp(target, 0f, 1f);
        FadeRate = seconds <= 0f ? 1000f : Math.Abs(FadeTarget - FadeGain) / seconds;
        StopAfterFade = stopWhenSilent && FadeTarget <= 0f;
    }

    /// <summary>Fades out over <paramref name="seconds"/>, then stops.</summary>
    public void FadeOut(float seconds) => FadeTo(0f, seconds, stopWhenSilent: true);

    /// <summary>Jumps to a time in the file.</summary>
    public void Seek(float seconds)
    {
        var clip = Clip;
        if (clip == null) return;
        Cursor = Math.Clamp(seconds, 0f, clip.Duration) * AudioClip.SampleRate;
    }

    /// <summary>Plays a file once, fire and forget (no object to keep). <paramref name="at"/> makes it a 3D sound.</summary>
    public static Sound? PlayOnce(string file, float volume = 1f, Vector3? at = null, float pitch = 1f)
    {
        var sound = new Sound(file) { Volume = volume, Pitch = pitch, Transient = true, Name = System.IO.Path.GetFileNameWithoutExtension(file) };
        if (at is { } position)
        {
            sound.Spatial = true;
            sound.Position = position;
        }
        sound.Play();
        return sound.Active ? sound : null;
    }

    /// <summary>The master volume of the whole game, 0..1.</summary>
    public static float MasterVolume
    {
        get => AudioEngine.MasterVolume;
        set => AudioEngine.MasterVolume = Math.Clamp(value, 0f, 2f);
    }

    // ---- scene -----------------------------------------------------------------------------------

    /// <summary>Called by the scene every frame: follows its object and handles <see cref="PlayOnStart"/>.</summary>
    internal void Update(Scene scene)
    {
        if (Follow is { IsDestroyed: false } target) Position = target.WorldPosition;

        if (scene.Simulate)
        {
            if (PlayOnStart && !_autoStarted)
            {
                _autoStarted = true;
                Play();
            }
        }
        else if (_autoStarted)
        {
            _autoStarted = false;
            Stop();   // the editor stopped playing: silence what the game started
        }

        if (!Enabled && Active) Stop();
    }

    public void Dispose() => Stop();
}
