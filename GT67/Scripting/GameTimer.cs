namespace GT67.Scripting;

/// <summary>
/// A delayed or repeating action started with <c>Game.After</c> or <c>Game.Every</c>. Timers follow
/// <c>Game.TimeScale</c>, only run while the game is playing, and are cancelled when the editor stops.
/// </summary>
public sealed class GameTimer
{
    private readonly Action _action;

    internal GameTimer(float seconds, bool repeat, Action action)
    {
        Interval = Math.Max(0f, seconds);
        Repeat = repeat;
        Remaining = Interval;
        _action = action;
    }

    /// <summary>Seconds between runs (repeating) or before the single run.</summary>
    public float Interval { get; set; }

    /// <summary>True for <c>Game.Every</c>: runs again every <see cref="Interval"/> seconds until cancelled.</summary>
    public bool Repeat { get; }

    /// <summary>Seconds left before the next run.</summary>
    public float Remaining { get; private set; }

    /// <summary>A paused timer keeps its remaining time and does not run.</summary>
    public bool Paused { get; set; }

    /// <summary>True once it has run (single timers) or was cancelled.</summary>
    public bool IsDone { get; private set; }

    /// <summary>Stops the timer; it will not run again.</summary>
    public void Cancel() => IsDone = true;

    /// <summary>Advances the timer; returns false when it is finished and can be dropped.</summary>
    internal bool Tick(float deltaTime)
    {
        if (IsDone) return false;
        if (Paused) return true;

        Remaining -= deltaTime;
        if (Remaining > 0f) return true;

        try
        {
            _action();
        }
        catch (Exception e)
        {
            IsDone = true; // do not repeat the error
            Utils.Log.Error($"Timer action failed and was cancelled: {e.Message}");
            return false;
        }

        if (!Repeat || IsDone)
        {
            IsDone = true;
            return false;
        }
        // Keep the overshoot so a repeating timer does not drift, but never fire more than once per frame.
        Remaining = Math.Max(Remaining + Interval, 0f);
        return true;
    }
}
