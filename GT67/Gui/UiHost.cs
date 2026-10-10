using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Common.Input;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GT67.Gui;

/// <summary>
/// Connects the <see cref="Gui"/> toolkit to a window: collects the mouse/keyboard input, runs a frame and draws the
/// result over the 3D scene. UI coordinates are window points; the surface is rendered at framebuffer resolution.
/// </summary>
public sealed class UiHost : IDisposable
{
    private readonly GameWindow _window;
    private readonly UiSurface _surface;
    private readonly UiFonts _fonts;
    private readonly GuiInput _input = new();
    private bool _inFrame;

    public UiHost(GameWindow window)
    {
        _window = window;
        string baseDir = AppContext.BaseDirectory;
        _fonts = new UiFonts(Path.Combine(baseDir, "Assets", "Fonts"));
        _surface = new UiSurface(Path.Combine(baseDir, "Shaders"));
        Gui = new Gui(_fonts);

        _input.GetClipboard = () => _window.ClipboardString ?? "";
        _input.SetClipboard = text => _window.ClipboardString = text;
    }

    public Gui Gui { get; }

    /// <summary>True while the mouse is over the UI (the game should not react to it).</summary>
    public bool WantCaptureMouse => Gui.WantCaptureMouse;

    /// <summary>True while a text box has the keyboard.</summary>
    public bool WantTextInput => Gui.WantTextInput;

    /// <summary>The size of the UI area in window points.</summary>
    public Vector2 Size => new(_window.ClientSize.X, _window.ClientSize.Y);

    /// <summary>Starts a frame: submit windows with <see cref="Gui"/> until <see cref="End"/>.</summary>
    public void Begin(float deltaTime)
    {
        _input.MousePosition = new Vector2(_window.MousePosition.X, _window.MousePosition.Y);
        UpdateModifiers();
        var size = Size;
        Gui.NewFrame(_input, MathF.Max(1f, size.X), MathF.Max(1f, size.Y), deltaTime);
        _inFrame = true;
    }

    /// <summary>Draws the frame onto the window's framebuffer.</summary>
    public void End()
    {
        if (!_inFrame) return;
        _inFrame = false;

        var fb = _window.FramebufferSize;
        if (fb.X > 0 && fb.Y > 0)
        {
            _surface.Begin(fb.X, fb.Y);
            var canvas = _surface.Canvas;
            canvas.Save();
            canvas.Scale(fb.X / MathF.Max(1f, Size.X), fb.Y / MathF.Max(1f, Size.Y));
            Gui.Render(canvas);
            canvas.Restore();
            _surface.Present();
        }
        _input.EndFrame();

        var shape = Gui.Cursor switch
        {
            CursorShape.IBeam => MouseCursor.IBeam,
            CursorShape.ResizeH => MouseCursor.ResizeEW,
            CursorShape.ResizeV => MouseCursor.ResizeNS,
            _ => MouseCursor.Default,
        };
        if (_window.CursorState == CursorState.Normal) _window.Cursor = shape;
    }

    // ---- input from the window's events ----------------------------------------------------------

    public void MouseDown(int button)
    {
        _input.MousePosition = new Vector2(_window.MousePosition.X, _window.MousePosition.Y);
        _input.MouseButtonDown(button, Gui.Time);
    }

    public void MouseUp(int button) => _input.MouseButtonUp(button);

    public void Wheel(float amount) => _input.Wheel += amount;

    public void Text(string text) => _input.TextTyped(text);

    public void Key(Keys key)
    {
        UpdateModifiers();
        _input.KeyDown(key);
    }

    private void UpdateModifiers()
    {
        var k = _window.KeyboardState;
        _input.Ctrl = k.IsKeyDown(Keys.LeftControl) || k.IsKeyDown(Keys.RightControl) ||
                      (OperatingSystem.IsMacOS() && (k.IsKeyDown(Keys.LeftSuper) || k.IsKeyDown(Keys.RightSuper)));
        _input.Shift = k.IsKeyDown(Keys.LeftShift) || k.IsKeyDown(Keys.RightShift);
        _input.Alt = k.IsKeyDown(Keys.LeftAlt) || k.IsKeyDown(Keys.RightAlt);
    }

    public void Dispose()
    {
        Gui.Dispose();
        _surface.Dispose();
        _fonts.Dispose();
    }
}
