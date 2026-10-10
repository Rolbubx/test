using System.Text;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GT67.Gui;

/// <summary>A key press (including auto-repeat) with the modifiers held at that moment.</summary>
public readonly record struct KeyEvent(Keys Key, bool Ctrl, bool Shift, bool Alt);

/// <summary>
/// Everything the UI needs to know about the mouse and keyboard for one frame. The window fills it from its events
/// and hands it to <see cref="Gui.NewFrame"/>; <see cref="EndFrame"/> clears what only lasts a single frame.
/// </summary>
public sealed class GuiInput
{
    private const float DoubleClickSeconds = 0.35f;
    private readonly float[] _lastClickTime = { -10f, -10f, -10f };
    private readonly Vector2[] _lastClickPosition = new Vector2[3];

    /// <summary>Mouse position in UI points.</summary>
    public Vector2 MousePosition { get; set; }

    public bool[] MouseDown { get; } = new bool[3];
    public bool[] MousePressed { get; } = new bool[3];
    public bool[] MouseReleased { get; } = new bool[3];
    public bool[] MouseDoubleClicked { get; } = new bool[3];

    /// <summary>Wheel movement this frame (positive = scroll up).</summary>
    public float Wheel { get; set; }

    public StringBuilder Typed { get; } = new();
    public List<KeyEvent> KeyPresses { get; } = new();

    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }

    /// <summary>Reads and writes the system clipboard (set by the window).</summary>
    public Func<string>? GetClipboard { get; set; }
    public Action<string>? SetClipboard { get; set; }

    public void MouseButtonDown(int button, float time)
    {
        if (button is < 0 or > 2) return;
        MouseDown[button] = true;
        MousePressed[button] = true;
        if (time - _lastClickTime[button] < DoubleClickSeconds &&
            (MousePosition - _lastClickPosition[button]).LengthSquared < 25f)
        {
            MouseDoubleClicked[button] = true;
            _lastClickTime[button] = -10f;
        }
        else
        {
            _lastClickTime[button] = time;
            _lastClickPosition[button] = MousePosition;
        }
    }

    public void MouseButtonUp(int button)
    {
        if (button is < 0 or > 2) return;
        MouseDown[button] = false;
        MouseReleased[button] = true;
    }

    public void KeyDown(Keys key) => KeyPresses.Add(new KeyEvent(key, Ctrl, Shift, Alt));

    public void TextTyped(string text)
    {
        foreach (char c in text)
            if (!char.IsControl(c)) Typed.Append(c);
    }

    /// <summary>True if the key was pressed (or repeated) this frame.</summary>
    public bool WasPressed(Keys key) => KeyPresses.Any(k => k.Key == key);

    /// <summary>Forgets everything that only lasts one frame (presses, releases, typed text, wheel).</summary>
    public void EndFrame()
    {
        Array.Clear(MousePressed);
        Array.Clear(MouseReleased);
        Array.Clear(MouseDoubleClicked);
        Wheel = 0f;
        Typed.Clear();
        KeyPresses.Clear();
    }
}
