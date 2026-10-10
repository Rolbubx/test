namespace GT67.Core;

/// <summary>Which of an object's key callbacks to run.</summary>
public enum KeyAction
{
    /// <summary>The key went down this frame (not repeated while held).</summary>
    Down,

    /// <summary>The key is currently down (every frame).</summary>
    Held,

    /// <summary>The key was released.</summary>
    Up,
}
