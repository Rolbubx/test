using GT67.Editor;
using GT67.UI;
using GT67.Utils;
using GT67.Gui;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GT67.Core;

/// <summary>
/// Application window: owns the scene, camera, renderer and UI, and runs the update/render loop.
/// Escape quits, F1 toggles mouse capture, F5 reloads shaders. Hold right mouse to look around.
/// </summary>
public sealed class Window : GameWindow
{
    private readonly GLDebugger _glDebugger = new();
    private readonly int _maxFrames;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>Command line "--novsync": measure raw speed instead of the 60 fps cap.</summary>
    public bool UncappedFps { get; set; }
    private int _frameCount;
    private bool _skipNextMouseDelta;
    private bool _lookHeld;
    private Vector2i _renderSize;
    private float _lastDelta = 1f / 60f;

    private static readonly Keys[] AllKeys = Enum.GetValues<Keys>().Where(k => k != Keys.Unknown).ToArray();

    public Scene Scene { get; } = new();

    /// <summary>The API scripts use (time, input, finding and spawning objects).</summary>
    public Scripting.Game Game { get; }
    public Camera Camera { get; private set; } = null!;
    public Renderer Renderer { get; private set; } = null!;

    /// <summary>The Skia-based UI shared by the debug panels, the editor and the game's own UI.</summary>
    public UiHost Ui { get; private set; } = null!;

    /// <summary>The game's user interface (HUD, menus); scripts reach it as <c>Game.Ui</c>.</summary>
    public UI.UiCanvas GameUi { get; } = new();

    /// <summary>The part of the window the 3D scene is drawn in, in window points (all of it unless the editor docks panels around it).</summary>
    public UiRect Viewport { get; set; }

    /// <summary>True while the right mouse button is held to look around.</summary>
    public bool IsLooking => _lookHeld;

    public DebugUI DebugUI { get; private set; } = null!;

    /// <summary>Turns the window into the map editor (set before Run; see IsEditor in Program.cs).</summary>
    public bool IsEditor { get; set; }

    /// <summary>The map editor, when <see cref="IsEditor"/> is on.</summary>
    public MapEditor? Editor { get; private set; }

    /// <summary>Called once the GL context is ready; build the scene here.</summary>
    public Action<Window>? Initialize { get; set; }

    /// <summary>Called every update tick, before the scene updates.</summary>
    public Action<float>? OnFrameUpdate { get; set; }

    /// <summary>WASD/QE fly camera. Turn off when objects use WASD themselves (e.g. a player model).</summary>
    public bool FreeCamera { get; set; } = true;

    /// <summary>Stop updating the world (and reading input) while the window is not the active one.</summary>
    public bool PauseWhenUnfocused { get; set; } = true;

    /// <param name="maxFrames">Closes automatically after this many frames (0 = run until closed).</param>
    public Window(string title, int width = 1280, int height = 720, int maxFrames = 0)
        : base(GameWindowSettings.Default, new NativeWindowSettings
        {
            ClientSize = new Vector2i(width, height),
            Title = title,
            API = ContextAPI.OpenGL,
            APIVersion = new Version(4, 1),
            Profile = ContextProfile.Core,
            Flags = ContextFlags.ForwardCompatible | DebugFlag(),
            // MSAA is done on our own render target, not the window's framebuffer.
            NumberOfSamples = 0,
        })
    {
        _maxFrames = maxFrames;
        Game = new Scripting.Game(this);
        Scene.Game = Game;
    }

    private static ContextFlags DebugFlag()
    {
#if DEBUG
        return OperatingSystem.IsMacOS() ? ContextFlags.Default : ContextFlags.Debug; // macOS has no KHR_debug
#else
        return ContextFlags.Default;
#endif
    }

    protected override void OnLoad()
    {
        base.OnLoad();

#if DEBUG
        _glDebugger.Enable();
#endif
        Log.Info($"OpenGL {GL.GetString(StringName.Version)} | {GL.GetString(StringName.Renderer)}");

        VSync = UncappedFps ? VSyncMode.Off : VSyncMode.On;

        var fb = FramebufferSize;
        Renderer = new Renderer(Path.Combine(AppContext.BaseDirectory, "Shaders"), fb.X, fb.Y);
        Camera = new Camera(new Vector3(0, 3, 9), (float)fb.X / Math.Max(1, fb.Y));
        Camera.LookAt(new Vector3(0, 1, 0));

        Ui = new UiHost(this);
        DebugUI = new DebugUI(this);
        GameUi.QuitRequested += Close;
        Viewport = new UiRect(0f, 0f, ClientSize.X, ClientSize.Y);

        Initialize?.Invoke(this);
#if !GAME
        if (IsEditor) Editor = new MapEditor(this);
#endif
    }

    protected override void OnUnload()
    {
        base.OnUnload();
        AudioEngine.Shutdown();
        Editor?.Dispose();
        DebugUI.Dispose();
        Ui.Dispose();
        GameUi.Dispose();
        Scene.Dispose();
        ModelCache.Clear();
        Renderer.Dispose();
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        if (Renderer == null) return;
        Viewport = new UiRect(0f, 0f, ClientSize.X, ClientSize.Y);
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);
        if (PauseWhenUnfocused && !IsFocused) return;

        float dt = (float)args.Time;
        bool typing = Ui.WantTextInput;

        // Scripts' TimeScale slows down (or pauses) the world; the camera and the editor keep real time.
        float gameDt = dt * Game.TimeScale;

        _lastDelta = dt;
        Game.UnscaledDeltaTime = dt;
        Game.DeltaTime = gameDt;
        Game.Time += gameDt;
        Game.Frame++;

        // A menu in the game UI asked for the mouse: let go of the captured cursor.
        if (GameUi.ShowCursor && CursorState == CursorState.Grabbed) CursorState = CursorState.Normal;

        // In the editor the camera only flies while the right mouse button is held, so WASD stays free for other uses.
        bool fly = (FreeCamera || Game.ViewDetached) && (Editor == null || _lookHeld);
        if (fly && !typing) Camera.Move(KeyboardState, dt);

        if (CursorState == CursorState.Grabbed || _lookHeld)
        {
            if (_skipNextMouseDelta) _skipNextMouseDelta = false;
            else Camera.Look(MouseState.Delta.X, MouseState.Delta.Y);
        }

        AudioEngine.SetListener(Camera.Position, Camera.Right);
        OnFrameUpdate?.Invoke(dt);
        if (!typing) DispatchHeldKeys(gameDt);
        Game.Tick(gameDt);
        Scene.Update(gameDt);
        Editor?.Update(dt);
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);
        float dt = (float)args.Time;

        var fb = FramebufferSize;
        Framebuffer.BindDefault(0, 0, fb.X, fb.Y);
        GL.ClearColor(0.055f, 0.06f, 0.075f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        Ui.Begin(dt);
        Viewport = new UiRect(0f, 0f, ClientSize.X, ClientSize.Y);
        if (Editor != null) Editor.Draw(dt);
        else DebugUI.Draw(dt);
        GameUi.Draw(Ui.Gui, Viewport, interactive: Editor == null || Editor.Playing);

        ApplyViewport();

        // Outlining a whole terrain mesh would cover the screen in lines, so the terrain is never outlined.
        var selected = DebugUI.Selected;
        Renderer.Highlight = selected != null && Scene.Terrains.All(t => t.Object != selected) ? selected : null;
        Renderer.Render(Scene, Camera);
        Renderer.ReportFrameTime(dt);

        Ui.End();
        SwapBuffers();

#if DEBUG
        _glDebugger.Poll("frame");
#endif
        if (_maxFrames > 0 && ++_frameCount >= _maxFrames)
        {
            Log.Info($"Smoke test: {_frameCount} frames in {_clock.Elapsed.TotalSeconds:0.0} s = {_frameCount / _clock.Elapsed.TotalSeconds:0} fps average.");
            Close();
        }
    }

    /// <summary>Makes the renderer draw into <see cref="Viewport"/> (a part of the window while the editor panels surround it).</summary>
    private void ApplyViewport()
    {
        var fb = FramebufferSize;
        float sx = fb.X / (float)Math.Max(1, ClientSize.X), sy = fb.Y / (float)Math.Max(1, ClientSize.Y);
        int w = Math.Max(1, (int)MathF.Round(Viewport.Width * sx));
        int h = Math.Max(1, (int)MathF.Round(Viewport.Height * sy));
        int x = (int)MathF.Round(Viewport.X * sx);
        int y = fb.Y - (int)MathF.Round(Viewport.Y * sy) - h;

        if (_renderSize.X != w || _renderSize.Y != h)
        {
            Renderer.Resize(w, h);
            _renderSize = new Vector2i(w, h);
        }
        Renderer.OutputOffset = new Vector2i(x, y);
        Camera.AspectRatio = (float)w / h;
    }

    protected override void OnKeyDown(KeyboardKeyEventArgs e)
    {
        base.OnKeyDown(e);
        Ui.Key(e.Key);
        if (e.IsRepeat || Ui.WantTextInput) return;

        switch (e.Key)
        {
            case Keys.Escape when Editor == null:
                Close();
                break;
            case Keys.F1:
                ToggleMouseCapture();
                break;
            case Keys.F2:
                DebugUI.Visible = !DebugUI.Visible;
                break;
            case Keys.F5:
                ReloadShaders();
                break;
        }

        Editor?.OnKeyDown(e.Key);
        DispatchKey(KeyAction.Down, e.Key);
    }

    protected override void OnKeyUp(KeyboardKeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (!Ui.WantTextInput) DispatchKey(KeyAction.Up, e.Key);
    }

    // ---- object key callbacks --------------------------------------------------------------------

    private void DispatchKey(KeyAction action, Keys key)
    {
        if (!Scene.HasKeyCallbacks(action)) return;
        Scene.DispatchKey(action, key, GetMouseWorldPosition(), Camera.Position, _lastDelta);
    }

    private void DispatchHeldKeys(float deltaTime)
    {
        if (!Scene.HasKeyCallbacks(KeyAction.Held)) return;

        var mouse = GetMouseWorldPosition();
        foreach (var key in AllKeys)
            if (KeyboardState.IsKeyDown(key))
                Scene.DispatchKey(KeyAction.Held, key, mouse, Camera.Position, deltaTime);
    }

    /// <summary>The mouse position inside <see cref="Viewport"/>, in points from its top-left corner.</summary>
    public Vector2 MouseInViewport => new(MousePosition.X - Viewport.X, MousePosition.Y - Viewport.Y);

    /// <summary>A world-space ray through the mouse cursor (through the viewport centre while the cursor is captured).</summary>
    public Ray GetMouseRay()
    {
        var size = new Vector2(Viewport.Width, Viewport.Height);
        var point = CursorState == CursorState.Grabbed ? size * 0.5f : MouseInViewport;
        return Camera.ScreenPointToRay(point, size);
    }

    /// <summary>
    /// The world point under the mouse cursor (the screen centre while the cursor is captured): where the cursor
    /// ray hits the scene, else where it crosses the ground plane (y = 0), else 100 units along the ray.
    /// </summary>
    public Vector3 GetMouseWorldPosition()
    {
        var ray = GetMouseRay();

        if (Scene.Raycast(ray.Origin, ray.Direction, out float distance) != null) return ray.GetPoint(distance);
        if (ray.Direction.Y < -1e-4f) return ray.GetPoint(-ray.Origin.Y / ray.Direction.Y);
        return ray.GetPoint(100f);
    }

    public void ReloadShaders()
    {
        Log.Info(Renderer.ReloadShaders() ? "All shaders reloaded." : "Some shaders failed to reload (see log).");
    }

    public void ToggleMouseCapture()
    {
        CursorState = CursorState == CursorState.Grabbed ? CursorState.Normal : CursorState.Grabbed;
        _skipNextMouseDelta = true;
    }

    // ---- mouse and text --------------------------------------------------------------------------

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Ui.Wheel(e.OffsetY);
        if (!Ui.Gui.IsPointOverUi(MousePosition)) Camera.Zoom(e.OffsetY);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        Ui.Text(e.AsString);
    }

    protected override void OnFileDrop(FileDropEventArgs e)
    {
        base.OnFileDrop(e);
        Editor?.OnFileDrop(e.FileNames);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Ui.MouseDown((int)e.Button);
        if (Ui.Gui.IsPointOverUi(MousePosition) || !Viewport.Contains(MousePosition)) return;

        if (e.Button == MouseButton.Right && CursorState != CursorState.Grabbed)
        {
            _lookHeld = true;
            _skipNextMouseDelta = true;
        }
        if (e.Button != MouseButton.Left || CursorState == CursorState.Grabbed) return;

        if (Editor != null)
        {
            Editor.OnMouseDown(e);
            return;
        }

        // Click-to-select while the debug window is open.
        if (!DebugUI.Visible) return;
        var ray = GetMouseRay();
        DebugUI.Selected = Scene.Raycast(ray.Origin, ray.Direction);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        Ui.MouseUp((int)e.Button);
        if (e.Button == MouseButton.Right) _lookHeld = false;
    }
}
