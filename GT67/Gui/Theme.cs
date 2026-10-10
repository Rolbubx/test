using SkiaSharp;

namespace GT67.Gui;

/// <summary>Colours and sizes of every widget. Change them to restyle the whole UI.</summary>
public sealed class Theme
{
    // ---- surfaces --------------------------------------------------------------------------------
    public SKColor Window = new(27, 29, 35);
    public SKColor WindowBorder = new(46, 50, 59);
    public SKColor TitleBar = new(34, 37, 45);
    public SKColor Frame = new(18, 20, 24);             // text boxes, slider tracks
    public SKColor FrameHover = new(24, 27, 32);
    public SKColor FrameActive = new(10, 12, 16);
    public SKColor Border = new(52, 57, 68);
    public SKColor BorderHover = new(78, 86, 104);
    public SKColor Header = new(38, 42, 51);            // section headers, tab strip
    public SKColor HeaderHover = new(48, 53, 64);
    public SKColor Separator = new(46, 50, 59);

    // ---- text ------------------------------------------------------------------------------------
    public SKColor Text = new(228, 231, 238);
    public SKColor TextDim = new(139, 146, 163);
    public SKColor TextDisabled = new(92, 98, 112);

    // ---- interaction -----------------------------------------------------------------------------
    public SKColor Accent = new(76, 141, 255);
    public SKColor AccentHover = new(110, 164, 255);
    public SKColor AccentPressed = new(52, 112, 224);
    public SKColor Button = new(48, 53, 64);
    public SKColor ButtonHover = new(60, 66, 80);
    public SKColor ButtonPressed = new(40, 44, 54);
    public SKColor Danger = new(229, 83, 75);
    public SKColor Success = new(63, 185, 80);
    public SKColor Warning = new(222, 168, 52);
    public SKColor Selection = new(76, 141, 255, 70);   // selected rows, text selection
    public SKColor Hover = new(255, 255, 255, 14);      // row hover
    public SKColor ScrollThumb = new(88, 96, 114, 150);
    public SKColor Shadow = new(0, 0, 0, 120);

    // ---- axis colours (used by vector fields and gizmo hints) -------------------------------------
    public SKColor AxisX = new(240, 84, 84);
    public SKColor AxisY = new(98, 205, 98);
    public SKColor AxisZ = new(84, 140, 250);

    // ---- metrics (in UI points) ------------------------------------------------------------------
    public float FontSize = 13f;
    public float SmallFontSize = 11f;
    public float HeadingFontSize = 15f;
    public float Rounding = 6f;
    public float FrameHeight = 26f;
    public float RowHeight = 24f;
    public float ButtonHeight = 28f;
    public float TitleBarHeight = 30f;
    public float WindowPadding = 10f;
    public float ItemSpacing = 6f;
    public float IndentWidth = 14f;
    public float ScrollbarWidth = 6f;
    public float LabelRatio = 0.42f;
    public float LabelMaxWidth = 150f;

    /// <summary>The default dark theme.</summary>
    public static Theme Dark() => new();
}
