namespace GT67.Core;

/// <summary>What a terrain brush stroke does to the heights.</summary>
public enum TerrainBrush
{
    /// <summary>Pushes the ground up.</summary>
    Raise,

    /// <summary>Pushes the ground down.</summary>
    Lower,

    /// <summary>Averages each point with its neighbours.</summary>
    Smooth,

    /// <summary>Pulls the ground towards one height (the height where the stroke started).</summary>
    Flatten,

    /// <summary>Adds rough, random bumps.</summary>
    Noise,
}
