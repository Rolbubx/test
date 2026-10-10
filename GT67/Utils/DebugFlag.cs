namespace GT67.Utils;

/// <summary>
/// A temporary on/off switch for trying things out: check <c>DebugFlag.On</c> anywhere (engine code, scripts with
/// <c>GT67.Utils.DebugFlag.On</c>). The editor sets it from its settings menu (saved as "debug_flag" in
/// editor.settings.json); in game mode the <c>--debug</c> command-line option turns it on.
/// </summary>
public static class DebugFlag
{
    public static bool On { get; set; }
}
