using OpenTK.Mathematics;
using SkiaSharp;

namespace GT67.Gui;

public sealed partial class Gui
{
    private SKPicture? _overlayPicture;
    private UiRect _lastItemRect;
    private int _tipKey;
    private float _tipStart;

    // drag and drop
    private object? _dragPayload;
    private string _dragLabel = "";
    private int _pendingDragId;
    private Vector2 _pendingDragStart;
    private object? _dropPayload;
    private bool _dropOverUi;
    private bool _dropReady;

    private SKCanvas Overlay => _overlayCanvas!;

    private void BeginOverlay()
    {
        _overlayRecorder?.Dispose();
        _overlayRecorder = new SKPictureRecorder();
        _overlayCanvas = _overlayRecorder.BeginRecording(new SKRect(0, 0, DisplaySize.X, DisplaySize.Y));

        // A drag that finished since the last frame is delivered once.
        if (_dragPayload != null && Input.MouseReleased[0])
        {
            _dropPayload = _dragPayload;
            _dropOverUi = _hoveredWindow != 0;
            _dropReady = true;
            _dragPayload = null;
        }
        if (Input.MouseReleased[0]) _pendingDragId = 0;
    }

    private void FinishOverlay()
    {
        if (_dragPayload != null) DrawDragGhost();
        _overlayPicture = _overlayRecorder!.EndRecording();
        _overlayCanvas = null;
    }

    private void ClearDropState()
    {
        _dropReady = false;
        _dropPayload = null;
    }

    // ---- tooltips --------------------------------------------------------------------------------

    /// <summary>Shows <paramref name="text"/> near the mouse if the previous item has been hovered for a moment.</summary>
    public void Tooltip(string text)
    {
        var rect = _lastItemRect;
        if (_window == null || !IsHovered(rect) || IsDragging || _dragPayload != null)
        {
            if (_tipKey == HashCode.Combine(rect.X, rect.Y, rect.Width)) _tipKey = 0;
            return;
        }

        int key = HashCode.Combine(rect.X, rect.Y, rect.Width);
        if (_tipKey != key)
        {
            _tipKey = key;
            _tipStart = Time;
        }
        if (Time - _tipStart < 0.45f) return;

        var font = Fonts.Get(Theme.FontSize);
        var lines = Wrap(font, text, 280f);
        float width = lines.Max(l => font.MeasureText(l)) + 20f;
        float height = lines.Count * LineHeight + 14f;
        float x = Math.Clamp(Input.MousePosition.X + 14f, 4f, DisplaySize.X - width - 4f);
        float y = Input.MousePosition.Y + 20f;
        if (y + height > DisplaySize.Y - 4f) y = Input.MousePosition.Y - height - 8f;

        var box = new UiRect(x, y, width, height);
        DrawShadow(Overlay, box, 6f);
        FillRect(Overlay, box, 6f, new SKColor(14, 16, 20, 245));
        StrokeRect(Overlay, box.Deflate(0.5f), 6f, Theme.Border);
        for (int i = 0; i < lines.Count; i++)
            DrawText(Overlay, lines[i], new UiRect(x + 10f, y + 7f + i * LineHeight, width - 20f, LineHeight), Theme.Text, TextAlign.Left, Theme.FontSize, elide: false);
    }

    // ---- drag and drop ---------------------------------------------------------------------------

    /// <summary>
    /// Call right after an item to make it draggable: <paramref name="payload"/> is delivered with
    /// <see cref="TryGetDrop"/> when the mouse button is released.
    /// </summary>
    public void DragSource(string ghostLabel, object payload)
    {
        int id = HashCode.Combine(_lastItemRect.X, _lastItemRect.Y, ghostLabel);
        if (IsHovered(_lastItemRect) && Input.MousePressed[0])
        {
            _pendingDragId = id;
            _pendingDragStart = Input.MousePosition;
        }
        if (_pendingDragId == id && Input.MouseDown[0] && _dragPayload == null &&
            (Input.MousePosition - _pendingDragStart).LengthSquared > 36f)
        {
            _dragPayload = payload;
            _dragLabel = ghostLabel;
            _activeId = 0;
        }
    }

    /// <summary>True while something is being dragged out of the UI.</summary>
    public bool HasDragPayload => _dragPayload != null;

    /// <summary>
    /// True on the frame a drag was released: gives the payload and whether the mouse was over a UI window
    /// (otherwise it was dropped on the 3D view).
    /// </summary>
    public bool TryGetDrop(out object? payload, out bool overUi)
    {
        payload = _dropPayload;
        overUi = _dropOverUi;
        return _dropReady;
    }

    private void DrawDragGhost()
    {
        var font = Fonts.Get(Theme.FontSize, FontWeight.Medium);
        float width = font.MeasureText(_dragLabel) + 22f;
        var box = new UiRect(Input.MousePosition.X + 12f, Input.MousePosition.Y + 12f, width, 26f);
        FillRect(Overlay, box, 6f, new SKColor(Theme.Accent.Red, Theme.Accent.Green, Theme.Accent.Blue, 220));
        DrawText(Overlay, _dragLabel, box, SKColors.White, TextAlign.Center, Theme.FontSize, FontWeight.Medium);
    }

    // ---- popups ----------------------------------------------------------------------------------

    private int _popupId;
    private UiRect _popupAnchor;
    private bool _popupDrawn, _popupWasDrawn;
    private long _popupOpenedFrame;
    private readonly Dictionary<int, float> _popupHeights = new();
    private static readonly int PopupWindowId = HashCode.Combine(0x5eed, "##popup");

    private void UpdatePopupBeforeFrame()
    {
        if (_popupId == 0) return;

        bool grace = Frame - _popupOpenedFrame <= 1;
        bool clickedOutside = (Input.MousePressed[0] || Input.MousePressed[1]) &&
                              _hoveredWindow != PopupWindowId && !_popupAnchor.Contains(Input.MousePosition);
        if (!grace && (clickedOutside || !_popupWasDrawn || Input.WasPressed(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Escape)))
            _popupId = 0;
    }

    private void EndPopupFrame()
    {
        _popupWasDrawn = _popupDrawn;
        _popupDrawn = false;
    }

    /// <summary>Opens the popup named <paramref name="id"/> below <paramref name="anchor"/> (closes it if it is already open).</summary>
    public void TogglePopup(string id, UiRect anchor)
    {
        int pid = GetId(id);
        if (_popupId == pid) _popupId = 0;
        else OpenPopup(pid, anchor);
    }

    private void OpenPopup(int pid, UiRect anchor)
    {
        _popupId = pid;
        _popupAnchor = anchor;
        _popupOpenedFrame = Frame;
        _popupWasDrawn = true;
    }

    public void OpenPopupAt(string id, Vector2 position) =>
        OpenPopup(GetId(id), new UiRect(position.X, position.Y - 2f, 1f, 1f));

    public bool IsPopupOpen(string id) => _popupId == GetId(id);

    public void CloseCurrentPopup() => _popupId = 0;

    /// <summary>
    /// The contents of an open popup (a drop-down list, a menu, a colour picker). Returns false if it is not open;
    /// call <see cref="EndPopup"/> only when it returned true.
    /// </summary>
    public bool BeginPopup(string id, float width, float maxHeight = 320f)
    {
        int pid = GetId(id);
        if (_popupId != pid) return false;

        _popupHeights.TryGetValue(pid, out float known);
        float height = Math.Clamp(known <= 0f ? 60f : known, 24f, maxHeight);
        float x = Math.Clamp(_popupAnchor.X, 4f, MathF.Max(4f, DisplaySize.X - width - 4f));
        float y = _popupAnchor.Bottom + 3f;
        if (y + height > DisplaySize.Y - 4f) y = MathF.Max(4f, _popupAnchor.Y - height - 3f);

        _popupDrawn = true;
        PushId(pid);
        Begin("##popup", new UiRect(x, y, width, height), WindowFlags.Fixed | WindowFlags.Popup);
        return true;
    }

    public void EndPopup()
    {
        var w = _window!;
        int pid = _popupId;
        End();
        PopId();
        _popupHeights[pid] = w.ContentHeight;
    }
}
