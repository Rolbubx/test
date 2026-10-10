using OpenTK.Windowing.GraphicsLibraryFramework;
using SkiaSharp;

namespace GT67.Gui;

public sealed partial class Gui
{
    // Only one text box can have the keyboard focus, so its editing state is shared.
    private int _focusId;
    private long _focusDrawnFrame;
    private string _edit = "";
    private int _caret, _anchor;
    private float _editScroll;
    private float _caretBlinkStart;

    /// <summary>True on the frame the user pressed Enter in a text box (or left it).</summary>
    public bool LastInputEntered { get; private set; }

    private void StartEditing(int id, string text, bool selectAll)
    {
        _focusId = id;
        _focusDrawnFrame = Frame;
        _edit = text;
        _caret = text.Length;
        _anchor = selectAll ? 0 : text.Length;
        _editScroll = 0f;
        _caretBlinkStart = Time;
    }

    private void BeginNumberEdit(int id, float value, string format) =>
        StartEditing(id, value.ToString("0.#######", System.Globalization.CultureInfo.InvariantCulture), true);

    private bool NumberEdit(int id, UiRect rect, ref float value, float min, float max, string format)
    {
        bool edited = TextEditCore(id, rect, "", true, null, out string text);
        if (!edited) return false;
        if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed))
            return false;
        parsed = Math.Clamp(parsed, min, max);
        if (parsed == value) return false;
        value = parsed;
        return true;
    }

    /// <summary>A single-line text box with the label on the left. Returns true when the text changed.</summary>
    public bool InputText(string label, ref string value, string? hint = null)
    {
        var field = BeginField(label, Theme.FrameHeight);
        bool changed = TextEditCore(GetId(label), field.Deflate(0f, 2f), value, false, hint, out string text);
        if (changed) value = text;
        return changed;
    }

    /// <summary>A text box that fills the available width without a label (search boxes, names).</summary>
    public bool InputTextBare(string id, ref string value, string? hint = null, float width = 0f)
    {
        var rect = AllocItem(width, Theme.FrameHeight - 4f);
        bool changed = TextEditCore(GetId(id), rect, value, false, hint, out string text);
        if (changed) value = text;
        return changed;
    }

    private bool TextEditCore(int id, UiRect rect, string shown, bool numeric, string? hint, out string result)
    {
        bool focused = _focusId == id;
        bool hovered = IsHovered(rect) && (_activeId == 0 || _activeId == id);
        var font = Fonts.Get(Theme.FontSize);
        bool changed = false;
        LastInputEntered = false;
        if (hovered) Cursor = CursorShape.IBeam;

        if (hovered && Input.MousePressed[0])
        {
            if (!focused) StartEditing(id, shown, Input.MouseDoubleClicked[0]);
            focused = true;
            _activeId = id;
            if (!Input.MouseDoubleClicked[0])
            {
                _caret = CaretFromX(font, _edit, Input.MousePosition.X - (rect.X + 8f) + _editScroll);
                if (!Input.Shift) _anchor = _caret;
            }
            else
            {
                _anchor = 0;
                _caret = _edit.Length;
            }
            _caretBlinkStart = Time;
        }
        else if (focused && Input.MousePressed[0] && !hovered)
        {
            _focusId = 0;
            focused = false;
            LastInputEntered = true;
        }

        if (focused)
        {
            _focusDrawnFrame = Frame;
            if (_activeId == id && Input.MouseDown[0] && !Input.MousePressed[0])
                _caret = CaretFromX(font, _edit, Input.MousePosition.X - (rect.X + 8f) + _editScroll);
            else if (_activeId == id && !Input.MouseDown[0])
                _activeId = 0;

            changed = ProcessEditKeys(numeric);
            if (_focusId != id)
            {
                focused = false;
                LastInputEntered = true;
            }
            else
            {
                _caret = Math.Clamp(_caret, 0, _edit.Length);
                _anchor = Math.Clamp(_anchor, 0, _edit.Length);
            }
        }

        string text = focused ? _edit : shown;
        float hv = Fade(id, hovered || focused);
        FillRect(Canvas, rect, 5f, focused ? Theme.FrameActive : Lerp(Theme.Frame, Theme.FrameHover, hv));
        StrokeRect(Canvas, rect.Deflate(0.5f), 5f, focused ? Theme.Accent : Lerp(Theme.Border, Theme.BorderHover, hv));

        var inner = rect.Deflate(8f, 2f);
        Canvas.Save();
        Canvas.ClipRect(inner.ToSk());
        if (focused)
        {
            float caretX = font.MeasureText(text.AsSpan(0, Math.Min(_caret, text.Length)));
            if (caretX - _editScroll > inner.Width - 2f) _editScroll = caretX - inner.Width + 2f;
            if (caretX - _editScroll < 0f) _editScroll = caretX;
            _editScroll = MathF.Max(0f, _editScroll);

            if (_anchor != _caret)
            {
                int a = Math.Min(_anchor, _caret), b = Math.Max(_anchor, _caret);
                float x0 = font.MeasureText(text.AsSpan(0, a)) - _editScroll;
                float x1 = font.MeasureText(text.AsSpan(0, b)) - _editScroll;
                FillRect(Canvas, new UiRect(inner.X + x0, inner.Y + 2f, x1 - x0, inner.Height - 4f), 2f, WithAlpha(Theme.Accent, 120));
            }
            DrawText(Canvas, text, new UiRect(inner.X - _editScroll, inner.Y, 100000f, inner.Height), Theme.Text, TextAlign.Left, Theme.FontSize, elide: false);
            if (((int)((Time - _caretBlinkStart) * 2f)) % 2 == 0)
            {
                _paint.Style = SKPaintStyle.Stroke;
                _paint.StrokeWidth = 1.2f;
                _paint.Color = Theme.Text;
                float cx = inner.X + caretX - _editScroll;
                Canvas.DrawLine(cx, inner.Y + 4f, cx, inner.Bottom - 4f, _paint);
            }
        }
        else if (text.Length == 0 && hint != null)
        {
            DrawText(Canvas, hint, inner, Theme.TextDisabled, TextAlign.Left, Theme.FontSize);
        }
        else
        {
            DrawText(Canvas, text, inner, Theme.Text, TextAlign.Left, Theme.FontSize);
        }
        Canvas.Restore();

        result = text;
        return changed;
    }

    private static int CaretFromX(SKFont font, string text, float x)
    {
        if (x <= 0f) return 0;
        for (int i = 1; i <= text.Length; i++)
        {
            float w = font.MeasureText(text.AsSpan(0, i));
            float prev = font.MeasureText(text.AsSpan(0, i - 1));
            if (x < (w + prev) * 0.5f) return i - 1;
        }
        return text.Length;
    }

    private static bool AcceptsNumeric(char c) => char.IsDigit(c) || c is '-' or '+' or '.' or 'e' or 'E';

    private void DeleteSelection()
    {
        int a = Math.Min(_anchor, _caret), b = Math.Max(_anchor, _caret);
        _edit = _edit.Remove(a, b - a);
        _caret = _anchor = a;
    }

    private void Insert(string s, bool numeric)
    {
        var filtered = new System.Text.StringBuilder();
        foreach (char c in s)
        {
            if (char.IsControl(c)) continue;
            if (numeric)
            {
                char n = c == ',' ? '.' : c;
                if (!AcceptsNumeric(n)) continue;
                filtered.Append(n);
            }
            else filtered.Append(c);
        }
        if (filtered.Length == 0) return;
        if (_anchor != _caret) DeleteSelection();
        _edit = _edit.Insert(_caret, filtered.ToString());
        _caret = _anchor = _caret + filtered.Length;
    }

    /// <summary>Applies typed characters and editing keys to the focused text; returns true if the text changed.</summary>
    private bool ProcessEditKeys(bool numeric)
    {
        string before = _edit;

        if (Input.Typed.Length > 0) Insert(Input.Typed.ToString(), numeric);

        foreach (var key in Input.KeyPresses)
        {
            bool ctrl = key.Ctrl;
            switch (key.Key)
            {
                case Keys.Left:
                    _caret = ctrl ? WordLeft(_caret) : (_anchor != _caret && !key.Shift ? Math.Min(_anchor, _caret) : Math.Max(0, _caret - 1));
                    if (!key.Shift) _anchor = _caret;
                    break;
                case Keys.Right:
                    _caret = ctrl ? WordRight(_caret) : (_anchor != _caret && !key.Shift ? Math.Max(_anchor, _caret) : Math.Min(_edit.Length, _caret + 1));
                    if (!key.Shift) _anchor = _caret;
                    break;
                case Keys.Home:
                    _caret = 0;
                    if (!key.Shift) _anchor = 0;
                    break;
                case Keys.End:
                    _caret = _edit.Length;
                    if (!key.Shift) _anchor = _caret;
                    break;
                case Keys.Backspace:
                    if (_anchor != _caret) DeleteSelection();
                    else if (_caret > 0)
                    {
                        int from = ctrl ? WordLeft(_caret) : _caret - 1;
                        _edit = _edit.Remove(from, _caret - from);
                        _caret = _anchor = from;
                    }
                    break;
                case Keys.Delete:
                    if (_anchor != _caret) DeleteSelection();
                    else if (_caret < _edit.Length) _edit = _edit.Remove(_caret, 1);
                    break;
                case Keys.A when ctrl:
                    _anchor = 0;
                    _caret = _edit.Length;
                    break;
                case Keys.C when ctrl:
                    CopySelection();
                    break;
                case Keys.X when ctrl:
                    CopySelection();
                    if (_anchor != _caret) DeleteSelection();
                    break;
                case Keys.V when ctrl:
                    if (Input.GetClipboard != null) Insert(Input.GetClipboard(), numeric);
                    break;
                case Keys.Enter or Keys.KeyPadEnter or Keys.Tab or Keys.Escape:
                    _focusId = 0;
                    break;
            }
            _caretBlinkStart = Time;
        }
        return before != _edit;
    }

    private void CopySelection()
    {
        if (_anchor == _caret || Input.SetClipboard == null) return;
        int a = Math.Min(_anchor, _caret), b = Math.Max(_anchor, _caret);
        Input.SetClipboard(_edit[a..b]);
    }

    private int WordLeft(int index)
    {
        while (index > 0 && !char.IsLetterOrDigit(_edit[index - 1])) index--;
        while (index > 0 && char.IsLetterOrDigit(_edit[index - 1])) index--;
        return index;
    }

    private int WordRight(int index)
    {
        while (index < _edit.Length && char.IsLetterOrDigit(_edit[index])) index++;
        while (index < _edit.Length && !char.IsLetterOrDigit(_edit[index])) index++;
        return index;
    }
}
