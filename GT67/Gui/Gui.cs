using OpenTK.Mathematics;
using SkiaSharp;

namespace GT67.Gui;

[Flags]
public enum WindowFlags
{
    None = 0,

    /// <summary>Draw a title bar.</summary>
    Title = 1 << 0,

    /// <summary>The title bar can be dragged to move the window.</summary>
    Movable = 1 << 1,

    /// <summary>A grip in the bottom-right corner resizes the window.</summary>
    Resizable = 1 << 2,

    /// <summary>The title bar has a button that folds the window up.</summary>
    Collapsible = 1 << 3,

    /// <summary>The rectangle passed to Begin is used every frame (docked panels).</summary>
    Fixed = 1 << 4,

    /// <summary>No scrolling (content is just clipped).</summary>
    NoScroll = 1 << 5,

    /// <summary>No background, border or shadow (invisible hit areas such as splitters).</summary>
    NoBackground = 1 << 6,

    /// <summary>The window height follows its content.</summary>
    AutoFit = 1 << 7,

    /// <summary>Flat docked look: square corners and no shadow.</summary>
    Panel = 1 << 8,

    /// <summary>Drawn above everything else (menus, drop-downs).</summary>
    Popup = 1 << 9,

    /// <summary>
    /// Never blocks the mouse: items react only where no other window is (game HUDs and menus drawn under the editor).
    /// </summary>
    ClickThrough = 1 << 10,

    /// <summary>A normal floating tool window.</summary>
    Floating = Title | Movable | Resizable | Collapsible,
}

/// <summary>The mouse cursor the UI would like the window to show.</summary>
public enum CursorShape
{
    Arrow,
    IBeam,
    ResizeH,
    ResizeV,
}

public enum TextAlign
{
    Left,
    Center,
    Right,
}

/// <summary>
/// An immediate-mode UI drawn with Skia: call widgets every frame from your update/draw code and they both draw and
/// return what the user did (<c>if (gui.Button("Compile")) ...</c>). Windows are recorded as pictures and composited in
/// z-order, so floating windows overlap correctly. See <see cref="UiHost"/> for how it is wired to the window.
/// </summary>
public sealed partial class Gui : IDisposable
{
    private sealed class WindowState
    {
        public int Id;
        public string Name = "";
        public UiRect Rect;
        public UiRect HitRect;
        public bool Collapsed;
        public int Z;
        public WindowFlags Flags;
        public SKPictureRecorder? Recorder;
        public SKCanvas? Canvas;
        public SKPicture? Picture;
        public readonly Stack<Region> Regions = new();
        public float ContentHeight;
        public int IdBase;
    }

    private sealed class Region
    {
        public int Id;
        public UiRect Clip;          // the visible part of the region
        public UiRect VisibleClip;   // Clip intersected with all enclosing regions (for mouse tests)
        public float Left, Right;    // x range items may use
        public float ContentTop;     // y of the first item when not scrolled
        public float Scroll;
        public float CursorY;        // y for the next full-width item
        public float LineTop, LineHeight, PrevRight;
        public bool SameLine;
        public float SameLineOffset = -1f;
        public float Indent;
        public float MaxY;           // bottom of the lowest item
        public bool Scrollable = true;
        public float NextItemWidth;
        public float Padding;
        public UiRect? Frame;        // for child regions: the outer border rect
        public bool IsChild;
        public bool ScrollToEnd;
    }

    private sealed class ScrollInfo
    {
        public float Offset;
        public float ContentHeight;
    }

    private readonly SKPaint _paint = new() { IsAntialias = true };
    private readonly Dictionary<int, WindowState> _windows = new();
    private readonly List<WindowState> _frameWindows = new();
    private List<WindowState> _hitOrder = new();
    private readonly Stack<WindowState> _windowStack = new();
    private readonly Stack<int> _idStack = new();
    private readonly Dictionary<int, ScrollInfo> _scrolls = new();
    private readonly Dictionary<int, float> _anim = new();
    private WindowState? _window;
    private int _hoveredWindow, _activeId, _dragWindow, _resizeWindow, _zCounter = 1000;
    private Vector2 _dragOffset;
    private bool _wheelConsumed;
    private SKPictureRecorder? _overlayRecorder;
    private SKCanvas? _overlayCanvas;
    private bool _disposed;

    public Gui(UiFonts fonts, Theme? theme = null)
    {
        Fonts = fonts;
        Theme = theme ?? Theme.Dark();
    }

    public Theme Theme { get; }
    public UiFonts Fonts { get; }
    public GuiInput Input { get; private set; } = new();

    /// <summary>The size of the UI area in UI points.</summary>
    public Vector2 DisplaySize { get; private set; }

    /// <summary>Seconds since the UI started.</summary>
    public float Time { get; private set; }

    public float DeltaTime { get; private set; }
    public long Frame { get; private set; }

    /// <summary>True while the mouse is over a window or a widget is being dragged: the game should ignore the mouse.</summary>
    public bool WantCaptureMouse { get; private set; }

    /// <summary>The cursor shape to show this frame (reset every frame, set by widgets under the mouse).</summary>
    public CursorShape Cursor { get; private set; }

    /// <summary>True while a text box has the keyboard focus: the game should ignore typed keys.</summary>
    public bool WantTextInput => _focusId != 0;

    /// <summary>True if the point is over a window, over a click-through UI item, or a widget is being dragged.</summary>
    public bool IsPointOverUi(Vector2 point) =>
        _hitOrder.Any(w => w.HitRect.Contains(point)) || IsOverClickThrough(point) || _activeId != 0 || _dragWindow != 0 || _resizeWindow != 0 || _dragPayload != null;

    /// <summary>True while any widget is being held with the mouse.</summary>
    public bool IsDragging => _activeId != 0 || _dragWindow != 0 || _resizeWindow != 0;

    // ---- frame -----------------------------------------------------------------------------------

    /// <summary>Starts a frame. Windows begun after this call are drawn by <see cref="Render"/>.</summary>
    public void NewFrame(GuiInput input, float width, float height, float deltaTime)
    {
        Input = input;
        DisplaySize = new Vector2(width, height);
        DeltaTime = deltaTime;
        Time += deltaTime;
        Frame++;

        // Which window is under the mouse, judged by last frame's layout (top-most first).
        _hoveredWindow = 0;
        foreach (var w in _hitOrder)
        {
            if (w.HitRect.Contains(input.MousePosition))
            {
                _hoveredWindow = w.Id;
                break;
            }
        }

        // A widget that was being held while the button went up unnoticed (window lost focus, etc.).
        if (!input.MouseDown[0] && !input.MouseReleased[0] && !input.MouseDown[1] && !input.MouseReleased[1])
        {
            _activeId = 0;
            _dragWindow = 0;
            _resizeWindow = 0;
        }

        if ((input.MousePressed[0] || input.MousePressed[1]) && _hoveredWindow != 0)
            BringToFront(_hoveredWindow);

        UpdatePopupBeforeFrame();
        Cursor = CursorShape.Arrow;
        WantCaptureMouse = _hoveredWindow != 0 || IsOverClickThrough(input.MousePosition) || _activeId != 0 || _dragWindow != 0 || _resizeWindow != 0 || _dragPayload != null;

        _frameWindows.Clear();
        _wheelConsumed = false;
        BeginOverlay();
    }

    /// <summary>Draws everything submitted this frame onto <paramref name="canvas"/> (windows in z-order, then overlays).</summary>
    public void Render(SKCanvas canvas)
    {
        EndPopupFrame();
        if (_focusId != 0 && _focusDrawnFrame != Frame) _focusId = 0;
        FinishOverlay();

        var ordered = _frameWindows.OrderBy(w => w.Z).ToList();
        foreach (var w in ordered)
        {
            if (w.Picture != null) canvas.DrawPicture(w.Picture);
            w.Picture?.Dispose();
            w.Picture = null;
            w.Recorder?.Dispose();
            w.Recorder = null;
            w.Canvas = null;
        }
        if (_overlayPicture != null)
        {
            canvas.DrawPicture(_overlayPicture);
            _overlayPicture.Dispose();
            _overlayPicture = null;
        }

        ordered.Reverse();
        _hitOrder = ordered;
        (_passRects, _passRectsDone) = (_passRectsDone, _passRects);
        _passRects.Clear();

        // Drop windows that have not been drawn for a while so renamed/removed ones do not pile up.
        if (Frame % 600 == 0)
        {
            var stale = _windows.Values.Where(w => !_frameWindows.Contains(w) && !w.Name.StartsWith("##")).Select(w => w.Id).ToList();
            foreach (int id in stale) _windows.Remove(id);
        }
        ClearDropState();
    }

    private void BringToFront(int windowId)
    {
        if (_windows.TryGetValue(windowId, out var w) && !w.Flags.HasFlag(WindowFlags.Fixed) && !w.Flags.HasFlag(WindowFlags.Popup))
            w.Z = ++_zCounter;
    }

    // ---- ids -------------------------------------------------------------------------------------

    private int IdBase => _idStack.Count > 0 ? _idStack.Peek() : _window?.IdBase ?? 0;

    /// <summary>Hash of the label (text after "##" is part of the id but not shown).</summary>
    private int GetId(string label) => HashCode.Combine(IdBase, label);

    /// <summary>Makes ids unique inside a loop: <c>gui.PushId(i); ... gui.PopId();</c></summary>
    public void PushId(string id) => _idStack.Push(HashCode.Combine(IdBase, id));

    public void PushId(int id) => _idStack.Push(HashCode.Combine(IdBase, id));

    public void PopId() => _idStack.Pop();

    private static string Visible(string label)
    {
        int i = label.IndexOf("##", StringComparison.Ordinal);
        return i < 0 ? label : label[..i];
    }

    // ---- windows ---------------------------------------------------------------------------------

    /// <summary>
    /// Starts a window. For floating windows <paramref name="rect"/> is only the starting place (the user can move and
    /// resize it); for <see cref="WindowFlags.Fixed"/> windows it is used every frame. Always call <see cref="End"/>,
    /// even when this returns false (collapsed).
    /// </summary>
    public bool Begin(string name, UiRect rect, WindowFlags flags = WindowFlags.Floating)
    {
        int id = HashCode.Combine(0x5eed, name);
        if (!_windows.TryGetValue(id, out var w))
        {
            w = new WindowState { Id = id, Name = name, Rect = rect, IdBase = id };
            w.Z = flags.HasFlag(WindowFlags.Popup) ? 1_000_000 + ++_zCounter
                : flags.HasFlag(WindowFlags.Fixed) ? 10 : ++_zCounter;
            _windows[id] = w;
        }
        w.Flags = flags;
        if (flags.HasFlag(WindowFlags.Fixed)) w.Rect = rect;
        if (flags.HasFlag(WindowFlags.Popup)) w.Z = 1_000_000 + _zCounter;

        _windowStack.Push(w);
        _window = w;
        _frameWindows.Add(w);

        bool hasTitle = flags.HasFlag(WindowFlags.Title);
        float titleH = hasTitle ? Theme.TitleBarHeight : 0f;

        // Dragging and resizing are handled up front so the content is laid out at the new place.
        if (hasTitle && flags.HasFlag(WindowFlags.Movable) && _dragWindow == id)
        {
            if (Input.MouseDown[0])
            {
                var p = Input.MousePosition - _dragOffset;
                w.Rect = new UiRect(Math.Clamp(p.X, -w.Rect.Width + 80f, DisplaySize.X - 80f), Math.Clamp(p.Y, 0f, DisplaySize.Y - 24f), w.Rect.Width, w.Rect.Height);
            }
            else _dragWindow = 0;
        }
        if (flags.HasFlag(WindowFlags.Resizable) && _resizeWindow == id)
        {
            if (Input.MouseDown[0])
            {
                var p = Input.MousePosition;
                w.Rect = new UiRect(w.Rect.X, w.Rect.Y, MathF.Max(160f, p.X - w.Rect.X + 6f), MathF.Max(80f, p.Y - w.Rect.Y + 6f));
            }
            else _resizeWindow = 0;
        }

        var r = w.Rect;
        if (w.Collapsed) r = r.WithHeight(titleH);
        w.HitRect = flags.HasFlag(WindowFlags.NoBackground) && !flags.HasFlag(WindowFlags.Fixed) || flags.HasFlag(WindowFlags.ClickThrough) ? default : r;
        if (flags.HasFlag(WindowFlags.NoBackground) && flags.HasFlag(WindowFlags.Fixed) && !flags.HasFlag(WindowFlags.ClickThrough)) w.HitRect = r;

        // Start recording this window's drawing.
        w.Recorder = new SKPictureRecorder();
        w.Canvas = w.Recorder.BeginRecording(new SKRect(0, 0, DisplaySize.X, DisplaySize.Y));
        var canvas = w.Canvas;

        bool floating = !flags.HasFlag(WindowFlags.Panel) && !flags.HasFlag(WindowFlags.NoBackground);
        float rounding = flags.HasFlag(WindowFlags.Panel) || flags.HasFlag(WindowFlags.NoBackground) ? 0f : Theme.Rounding + 2f;

        if (!flags.HasFlag(WindowFlags.NoBackground))
        {
            if (floating) DrawShadow(canvas, r, rounding);
            FillRect(canvas, r, rounding, Theme.Window);
            StrokeRect(canvas, r.Deflate(0.5f), rounding, Theme.WindowBorder);
        }

        if (hasTitle) DrawTitleBar(w, r, titleH, rounding, flags);

        // Content region.
        w.Regions.Clear();
        if (w.Collapsed) return false;

        var content = new UiRect(r.X, r.Y + titleH, r.Width, r.Height - titleH);
        var scroll = GetScroll(id);
        var region = new Region
        {
            Id = id,
            Clip = content,
            VisibleClip = content,
            Padding = flags.HasFlag(WindowFlags.NoBackground) ? 0f : Theme.WindowPadding,
            Scrollable = !flags.HasFlag(WindowFlags.NoScroll),
            Scroll = scroll.Offset,
        };
        InitRegion(region, content);
        w.Regions.Push(region);

        canvas.Save();
        canvas.ClipRect(content.ToSk());

        // Resize grip (drawn after the content, in End; hit test here so it wins over content).
        return true;
    }

    /// <summary>Ends the window started with <see cref="Begin"/>.</summary>
    public void End()
    {
        var w = _window ?? throw new InvalidOperationException("End without Begin.");
        var canvas = w.Canvas!;
        var flags = w.Flags;
        bool hasTitle = flags.HasFlag(WindowFlags.Title);
        float titleH = hasTitle ? Theme.TitleBarHeight : 0f;

        if (!w.Collapsed && w.Regions.Count > 0)
        {
            var region = w.Regions.Pop();
            var content = region.Clip;
            float contentHeight = region.MaxY + region.Scroll - content.Y + region.Padding;
            w.ContentHeight = contentHeight;
            FinishScroll(w.Id, region, content, contentHeight, w.Id);
            canvas.Restore();

            if (flags.HasFlag(WindowFlags.AutoFit))
            {
                float desired = MathF.Min(titleH + contentHeight, DisplaySize.Y - w.Rect.Y - 8f);
                w.Rect = w.Rect.WithHeight(MathF.Max(desired, titleH + 20f));
            }

            if (flags.HasFlag(WindowFlags.Resizable)) DrawResizeGrip(w);
        }

        w.Picture = w.Recorder!.EndRecording();
        _windowStack.Pop();
        _window = _windowStack.Count > 0 ? _windowStack.Peek() : null;
    }

    private void DrawTitleBar(WindowState w, UiRect r, float titleH, float rounding, WindowFlags flags)
    {
        var canvas = w.Canvas!;
        var bar = new UiRect(r.X, r.Y, r.Width, titleH);

        // Title bar background (only the top corners are rounded).
        using (var path = new SKPath())
        {
            path.AddRoundRect(new SKRoundRect(bar.ToSk(), rounding, rounding));
            canvas.Save();
            canvas.ClipRect(new SKRect(bar.X, bar.Y, bar.Right, bar.Bottom));
            _paint.Style = SKPaintStyle.Fill;
            _paint.Color = Theme.TitleBar;
            canvas.DrawRoundRect(new SKRect(bar.X, bar.Y, bar.Right, bar.Bottom + rounding), rounding, rounding, _paint);
            canvas.Restore();
        }
        _paint.Color = Theme.WindowBorder;
        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = 1f;
        canvas.DrawLine(bar.X, bar.Bottom - 0.5f, bar.Right, bar.Bottom - 0.5f, _paint);

        bool hovered = _hoveredWindow == w.Id && bar.Contains(Input.MousePosition);
        float textX = bar.X + 12f;

        if (flags.HasFlag(WindowFlags.Collapsible))
        {
            var arrow = new UiRect(bar.X + 6f, bar.Y, 22f, titleH);
            var icon = w.Collapsed ? IconKind.ChevronRight : IconKind.ChevronDown;
            Icons.Draw(canvas, icon, arrow.Deflate(5f, 8f), Theme.TextDim, 1.5f);
            textX = bar.X + 30f;
            if (hovered && Input.MousePressed[0] && arrow.Contains(Input.MousePosition))
            {
                w.Collapsed = !w.Collapsed;
                return;
            }
        }

        if (hovered && Input.MouseDoubleClicked[0] && flags.HasFlag(WindowFlags.Collapsible))
        {
            w.Collapsed = !w.Collapsed;
            return;
        }

        if (hovered && Input.MousePressed[0] && flags.HasFlag(WindowFlags.Movable) && _activeId == 0 &&
            !new UiRect(bar.X, bar.Y, 30f, titleH).Contains(Input.MousePosition))
        {
            _dragWindow = w.Id;
            _dragOffset = Input.MousePosition - w.Rect.Position;
        }

        DrawText(canvas, Visible(w.Name), new UiRect(textX, bar.Y, bar.Right - textX - 8f, titleH), Theme.Text,
            TextAlign.Left, Theme.FontSize, FontWeight.SemiBold);
    }

    private void DrawResizeGrip(WindowState w)
    {
        var canvas = w.Canvas!;
        var r = w.Rect;
        var grip = new UiRect(r.Right - 16f, r.Bottom - 16f, 16f, 16f);
        bool hovered = _hoveredWindow == w.Id && grip.Contains(Input.MousePosition);
        if (hovered && Input.MousePressed[0] && _activeId == 0) _resizeWindow = w.Id;

        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = 1.4f;
        _paint.StrokeCap = SKStrokeCap.Round;
        _paint.Color = hovered || _resizeWindow == w.Id ? Theme.Text : Theme.TextDisabled;
        canvas.DrawLine(grip.Right - 4f, grip.Bottom - 10f, grip.Right - 10f, grip.Bottom - 4f, _paint);
        canvas.DrawLine(grip.Right - 4f, grip.Bottom - 5f, grip.Right - 5f, grip.Bottom - 4f, _paint);
        _paint.StrokeCap = SKStrokeCap.Butt;
    }

    // ---- regions (window content and child areas) ------------------------------------------------

    private Region CurrentRegion => _window!.Regions.Peek();
    private SKCanvas Canvas => _window!.Canvas!;

    private ScrollInfo GetScroll(int id)
    {
        if (!_scrolls.TryGetValue(id, out var info)) _scrolls[id] = info = new ScrollInfo();
        return info;
    }

    private static void InitRegion(Region r, UiRect area)
    {
        r.Left = area.X + r.Padding;
        r.Right = area.Right - r.Padding;
        r.ContentTop = area.Y + r.Padding;
        r.CursorY = r.ContentTop - r.Scroll;
        r.MaxY = r.CursorY;
        r.LineTop = r.CursorY;
        r.LineHeight = 0f;
        r.PrevRight = r.Left;
    }

    /// <summary>
    /// Draws the scrollbar, applies the mouse wheel and remembers the content height for the next frame.
    /// </summary>
    private void FinishScroll(int scrollId, Region region, UiRect area, float contentHeight, int windowId)
    {
        var info = GetScroll(scrollId);
        info.ContentHeight = contentHeight;
        float visible = area.Height;
        float maxScroll = region.Scrollable ? MathF.Max(0f, contentHeight - visible) : 0f;

        float scroll = region.ScrollToEnd ? maxScroll : info.Offset;
        bool hovered = _hoveredWindow == windowId && region.VisibleClip.Contains(Input.MousePosition);
        if (maxScroll > 0f && hovered && Input.Wheel != 0f && !_wheelConsumed)
        {
            scroll -= Input.Wheel * 48f;
            _wheelConsumed = true;
        }

        if (maxScroll > 0f)
        {
            float trackH = area.Height - 8f;
            float thumbH = MathF.Max(24f, trackH * visible / contentHeight);
            float thumbY = area.Y + 4f + (trackH - thumbH) * (maxScroll > 0f ? scroll / maxScroll : 0f);
            var thumb = new UiRect(area.Right - Theme.ScrollbarWidth - 3f, thumbY, Theme.ScrollbarWidth, thumbH);
            var grab = thumb.Inflate(4f);

            int id = HashCode.Combine(scrollId, "scrollbar");
            bool over = hovered && grab.Contains(Input.MousePosition) && (_activeId == 0 || _activeId == id);
            if (over && Input.MousePressed[0])
            {
                _activeId = id;
                _dragOffset = new Vector2(0f, Input.MousePosition.Y - thumbY);
            }
            if (_activeId == id)
            {
                if (Input.MouseDown[0])
                {
                    float t = (Input.MousePosition.Y - _dragOffset.Y - (area.Y + 4f)) / MathF.Max(1f, trackH - thumbH);
                    scroll = Math.Clamp(t, 0f, 1f) * maxScroll;
                    thumb = thumb with { Y = area.Y + 4f + (trackH - thumbH) * (scroll / maxScroll) };
                }
                else _activeId = 0;
            }

            var color = over || _activeId == id ? new SKColor(Theme.ScrollThumb.Red, Theme.ScrollThumb.Green, Theme.ScrollThumb.Blue, 230) : Theme.ScrollThumb;
            FillRect(Canvas, thumb, Theme.ScrollbarWidth * 0.5f, color);
        }

        info.Offset = Math.Clamp(scroll, 0f, maxScroll);
    }

    /// <summary>
    /// A scrollable sub-area inside the current window (lists, logs). Call <see cref="EndChild"/> afterwards.
    /// </summary>
    public bool BeginChild(string name, float height, bool border = true)
    {
        var parent = CurrentRegion;
        int id = GetId(name);
        var rect = AllocItem(0f, height);
        var canvas = Canvas;

        if (border)
        {
            FillRect(canvas, rect, Theme.Rounding, Theme.Frame);
            StrokeRect(canvas, rect.Deflate(0.5f), Theme.Rounding, Theme.Border);
        }

        var scroll = GetScroll(id);
        var inner = border ? rect.Deflate(1f) : rect;
        var region = new Region
        {
            Id = id,
            Clip = inner,
            VisibleClip = UiRect.Intersect(inner, parent.VisibleClip),
            Padding = border ? 6f : 0f,
            Scroll = scroll.Offset,
            IsChild = true,
            Frame = rect,
        };
        InitRegion(region, inner);
        _window!.Regions.Push(region);

        canvas.Save();
        canvas.ClipRect(inner.ToSk());
        PushId(id);
        return true;
    }

    public void EndChild()
    {
        PopId();
        var region = _window!.Regions.Pop();
        float contentHeight = region.MaxY + region.Scroll - region.Clip.Y + region.Padding;
        FinishScroll(region.Id, region, region.Clip, contentHeight, _window.Id);
        Canvas.Restore();
    }

    // ---- layout ----------------------------------------------------------------------------------

    /// <summary>Width still free on the current line.</summary>
    public float AvailableWidth
    {
        get
        {
            var r = CurrentRegion;
            float x = r.SameLine ? r.PrevRight + Theme.ItemSpacing : r.Left + r.Indent;
            return MathF.Max(1f, r.Right - x);
        }
    }

    /// <summary>Scrolls the current window or child area to its end (logs).</summary>
    public void ScrollToEnd() => CurrentRegion.ScrollToEnd = true;

    /// <summary>Height still free below the cursor in the current window or child area.</summary>
    public float AvailableHeight
    {
        get
        {
            var r = CurrentRegion;
            return MathF.Max(24f, r.Clip.Bottom - r.Padding - r.CursorY);
        }
    }

    /// <summary>Width left on the current line after the item that was just drawn (to decide whether the next one fits).</summary>
    public float RemainingWidth
    {
        get
        {
            var r = CurrentRegion;
            return MathF.Max(0f, r.Right - (r.PrevRight + Theme.ItemSpacing));
        }
    }

    /// <summary>Puts the next item on the same line as the previous one.</summary>
    public void SameLine(float offset = -1f)
    {
        var r = CurrentRegion;
        r.SameLine = true;
        r.SameLineOffset = offset;
    }

    /// <summary>Forces the next item onto a new line.</summary>
    public void NewLine() => CurrentRegion.SameLine = false;

    /// <summary>Extra vertical space.</summary>
    public void Spacing(float height = 6f)
    {
        var r = CurrentRegion;
        r.SameLine = false;
        r.CursorY += height;
        r.MaxY = MathF.Max(r.MaxY, r.CursorY);
    }

    public void Indent(float width = -1f) => CurrentRegion.Indent += width < 0f ? Theme.IndentWidth : width;

    public void Unindent(float width = -1f) => CurrentRegion.Indent = MathF.Max(0f, CurrentRegion.Indent - (width < 0f ? Theme.IndentWidth : width));

    /// <summary>The width of the next item (applies once).</summary>
    public void SetNextItemWidth(float width) => CurrentRegion.NextItemWidth = width;

    /// <summary>Reserves a rectangle in the layout for custom drawing (width &lt;= 0: the rest of the line).</summary>
    public UiRect Reserve(float width, float height) => AllocItem(width, height);

    /// <summary>Reserves a rectangle in the layout. width &lt;= 0 means "all remaining width on the line".</summary>
    private UiRect AllocItem(float width, float height)
    {
        var r = CurrentRegion;
        if (_placed is { } forced)
        {
            _placed = null;
            _lastItemRect = forced;
            r.MaxY = MathF.Max(r.MaxY, forced.Bottom);
            return forced;
        }
        if (width <= 0f && r.NextItemWidth > 0f) width = r.NextItemWidth;
        r.NextItemWidth = 0f;

        float x, y;
        if (r.SameLine)
        {
            x = r.SameLineOffset >= 0f ? r.Left + r.Indent + r.SameLineOffset : r.PrevRight + Theme.ItemSpacing;
            y = r.LineTop;
            r.SameLine = false;
            r.SameLineOffset = -1f;
        }
        else
        {
            x = r.Left + r.Indent;
            y = r.CursorY;
            r.LineTop = y;
            r.LineHeight = 0f;
        }

        float w = width > 0f ? width : MathF.Max(1f, r.Right - x);
        var rect = new UiRect(x, y, w, height);

        r.PrevRight = rect.Right;
        r.LineHeight = MathF.Max(r.LineHeight, height);
        r.CursorY = r.LineTop + r.LineHeight + Theme.ItemSpacing;
        r.MaxY = MathF.Max(r.MaxY, rect.Bottom);
        _lastItemRect = rect;
        return rect;
    }

    /// <summary>The label column of a property row: draws the label and returns the rectangle left for the control.</summary>
    private UiRect BeginField(string label, float height)
    {
        var row = AllocItem(0f, height);
        string text = Visible(label);
        float labelWidth = MathF.Min(row.Width * Theme.LabelRatio, Theme.LabelMaxWidth);
        if (text.Length > 0)
            DrawText(Canvas, text, new UiRect(row.X, row.Y, labelWidth - 6f, height), Theme.TextDim, TextAlign.Left, Theme.FontSize);
        else labelWidth = 0f;
        return new UiRect(row.X + labelWidth, row.Y, row.Width - labelWidth, height);
    }

    // ---- hit testing -----------------------------------------------------------------------------

    private bool IsHovered(UiRect rect)
    {
        if (_window == null) return false;
        var p = Input.MousePosition;
        if (_window.Flags.HasFlag(WindowFlags.ClickThrough))
        {
            _passRects.Add(UiRect.Intersect(rect, CurrentRegion.VisibleClip));
            return _hoveredWindow == 0 && rect.Contains(p) && CurrentRegion.VisibleClip.Contains(p);
        }
        if (_hoveredWindow != _window.Id) return false;
        return rect.Contains(p) && CurrentRegion.VisibleClip.Contains(p);
    }

    // Areas of click-through windows (the game UI) that react to the mouse: this frame's and the finished previous frame's.
    private List<UiRect> _passRects = new(), _passRectsDone = new();
    private UiRect? _placed;

    private bool IsOverClickThrough(Vector2 point)
    {
        foreach (var rect in _passRectsDone)
            if (rect.Contains(point)) return true;
        return false;
    }

    /// <summary>Makes an area of a click-through window (a game UI panel) swallow the mouse like a window does.</summary>
    public void BlockMouse(UiRect rect)
    {
        if (_window != null && _window.Flags.HasFlag(WindowFlags.ClickThrough)) _passRects.Add(rect);
    }

    /// <summary>The next item is drawn exactly in <paramref name="rect"/> (window coordinates) instead of following the layout.</summary>
    public void PlaceNext(UiRect rect) => _placed = rect;

    /// <summary>
    /// Standard press/hold/release logic for a button-like rectangle. Returns true on the frame the button is clicked
    /// (released over the item).
    /// </summary>
    private bool ButtonBehavior(int id, UiRect rect, out bool hovered, out bool held)
    {
        hovered = IsHovered(rect) && (_activeId == 0 || _activeId == id);
        if (hovered && Input.MousePressed[0]) _activeId = id;

        held = _activeId == id;
        bool clicked = false;
        if (held && Input.MouseReleased[0])
        {
            clicked = hovered;
            _activeId = 0;
            held = false;
        }
        else if (held && !Input.MouseDown[0])
        {
            _activeId = 0;
            held = false;
        }
        return clicked;
    }

    /// <summary>A value that eases towards 0 or 1 (for hover and toggle animations).</summary>
    private float Fade(int id, bool on, float speed = 16f)
    {
        _anim.TryGetValue(id, out float v);
        float target = on ? 1f : 0f;
        v += (target - v) * (1f - MathF.Exp(-speed * MathF.Max(DeltaTime, 0.001f)));
        if (MathF.Abs(v - target) < 0.004f) v = target;
        _anim[id] = v;
        return v;
    }

    // ---- drawing primitives ----------------------------------------------------------------------

    private void FillRect(SKCanvas canvas, UiRect r, float radius, SKColor color)
    {
        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = color;
        if (radius > 0.5f) canvas.DrawRoundRect(r.ToSk(), radius, radius, _paint);
        else canvas.DrawRect(r.ToSk(), _paint);
    }

    private void StrokeRect(SKCanvas canvas, UiRect r, float radius, SKColor color, float width = 1f)
    {
        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = width;
        _paint.Color = color;
        if (radius > 0.5f) canvas.DrawRoundRect(r.ToSk(), radius, radius, _paint);
        else canvas.DrawRect(r.ToSk(), _paint);
        _paint.Style = SKPaintStyle.Fill;
    }

    private void DrawShadow(SKCanvas canvas, UiRect r, float radius)
    {
        using var shadow = new SKPaint
        {
            IsAntialias = true,
            Color = Theme.Shadow,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 9f),
        };
        canvas.DrawRoundRect(r.Offset(0f, 5f).ToSk(), radius, radius, shadow);
    }

    // ---- disposal --------------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _paint.Dispose();
        foreach (var w in _windows.Values)
        {
            w.Picture?.Dispose();
            w.Recorder?.Dispose();
        }
        _overlayRecorder?.Dispose();
        _overlayPicture?.Dispose();
    }
}
