using OpenTK.Mathematics;

namespace GT67.Gui;

/// <summary>
/// Property-row helpers: take the current value and a setter, so a panel reads
/// <c>gui.Slider("Exposure", post.Exposure, 0.1f, 4f, v =&gt; post.Exposure = v)</c> instead of copying values in and out.
/// </summary>
public static class GuiProps
{
    public static bool Slider(this Gui g, string label, float value, float min, float max, Action<float> set, string format = "0.00")
    {
        if (!g.SliderFloat(label, ref value, min, max, format)) return false;
        set(value);
        return true;
    }

    public static bool Drag(this Gui g, string label, float value, float speed, Action<float> set,
        float min = float.MinValue, float max = float.MaxValue)
    {
        if (!g.DragFloat(label, ref value, speed, min, max)) return false;
        set(value);
        return true;
    }

    public static bool Check(this Gui g, string label, bool value, Action<bool> set)
    {
        if (!g.Toggle(label, ref value)) return false;
        set(value);
        return true;
    }

    public static bool Drag3(this Gui g, string label, Vector3 value, float speed, Action<Vector3> set)
    {
        if (!g.DragVector3(label, ref value, speed)) return false;
        set(value);
        return true;
    }

    /// <summary>A colour row without alpha.</summary>
    public static bool Rgb(this Gui g, string label, Color4 value, Action<Color4> set)
    {
        var rgb = new Vector3(value.R, value.G, value.B);
        if (!g.ColorEdit(label, ref rgb)) return false;
        set(new Color4(rgb.X, rgb.Y, rgb.Z, value.A));
        return true;
    }

    public static bool Rgba(this Gui g, string label, Color4 value, Action<Color4> set)
    {
        var rgba = new Vector4(value.R, value.G, value.B, value.A);
        if (!g.ColorEdit(label, ref rgba)) return false;
        set(new Color4(rgba.X, rgba.Y, rgba.Z, rgba.W));
        return true;
    }

    public static bool Input(this Gui g, string label, string value, Action<string> set, string? hint = null)
    {
        if (!g.InputText(label, ref value, hint)) return false;
        set(value);
        return true;
    }

    public static bool Choose<T>(this Gui g, string label, T value, Action<T> set) where T : struct, Enum
    {
        if (!g.EnumCombo(label, ref value)) return false;
        set(value);
        return true;
    }
}
