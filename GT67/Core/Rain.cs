namespace GT67.Core;

/// <summary>
/// Rain for a scene: <c>scene.Rain = new Rain { Intensity = 0.7f };</c> (null = dry weather).
/// It draws the falling streaks and splashes, and drives the whole weather: the sky closes into a dark overcast
/// that hides the sun, fog thickens, the ground gets wet with rippling, mirroring puddles.
/// Change any setting while the game runs; <see cref="Intensity"/> 0 lets the weather clear up again.
/// </summary>
public sealed class Rain
{
    public bool Enabled { get; set; } = true;

    /// <summary>Amount of rain, 0 (none) to 1 (downpour).</summary>
    public float Intensity { get; set; } = 0.7f;

    /// <summary>Fall speed in metres per second.</summary>
    public float Speed { get; set; } = 14f;

    /// <summary>Length of a streak in metres.</summary>
    public float Length { get; set; } = 0.55f;

    /// <summary>Sideways drift per metre of fall (wind). 0 = straight down.</summary>
    public float Slant { get; set; } = 0.1f;

    public float Brightness { get; set; } = 1.3f;

    /// <summary>Brightness of the splashes where drops hit flat ground (0 = none).</summary>
    public float Splashes { get; set; } = 1f;

    /// <summary>How much the picture dims and greys under the rain, 0..1.</summary>
    public float Darken { get; set; } = 0.5f;

    /// <summary>How long the rain lasts in seconds, then it eases off and the sun comes back. 0 = rains forever.</summary>
    public float Duration { get; set; } = 90f;

    /// <summary>Seconds of dry weather after the rain before it starts again. 0 = it does not come back (call <see cref="Restart"/>).</summary>
    public float Pause { get; set; }

    /// <summary>Seconds the rain takes to build up and to ease off.</summary>
    public float FadeTime { get; set; } = 10f;

    private float _time;

    /// <summary>Seconds since this rain started (wraps when it repeats).</summary>
    public float Elapsed => _time;

    /// <summary>The rain is over (and will not return by itself).</summary>
    public bool Finished => Duration > 0f && Pause <= 0f && _time >= Duration;

    /// <summary>Starts the rain again from the beginning.</summary>
    public void Restart() => _time = 0f;

    /// <summary>Advances the weather clock. Called by <see cref="Scene.Update"/>.</summary>
    public void Update(float deltaTime)
    {
        if (!Enabled) return;
        _time += deltaTime;
        if (Duration > 0f && Pause > 0f && _time >= Duration + Pause) _time -= Duration + Pause;
    }

    /// <summary>The intensity the scene uses right now: 0 while disabled or over, easing in and out at the ends.</summary>
    public float Amount
    {
        get
        {
            if (!Enabled) return 0f;
            float fade = MathF.Max(FadeTime, 0.01f);
            float envelope = Math.Clamp(_time / fade, 0f, 1f);
            if (Duration > 0f) envelope = MathF.Min(envelope, Math.Clamp((Duration - _time) / fade, 0f, 1f));
            return Math.Clamp(Intensity, 0f, 1f) * envelope;
        }
    }
}
