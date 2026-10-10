using GT67.Core;
using GT67.Gui;
using OpenTK.Mathematics;

namespace GT67.UI;

/// <summary>Draws a widget for every public setting of an object (see <see cref="ParameterAccess"/>).</summary>
public static class ParameterEditor
{
    /// <summary>Returns true if any value was changed this frame.</summary>
    public static bool Draw(Gui.Gui g, object target)
    {
        bool changed = false;
        foreach (var parameter in ParameterAccess.Describe(target))
        {
            g.PushId(parameter.Name);
            changed |= DrawOne(g, parameter);
            g.PopId();
        }
        return changed;
    }

    private static bool DrawOne(Gui.Gui g, ParameterDescriptor p)
    {
        var type = p.Type;
        var value = p.Value;
        string label = Nicify(p.Name);

        if (type == typeof(float))
        {
            float v = (float)value!;
            return g.DragFloat(label, ref v, 0.05f) && p.TrySet(v);
        }
        if (type == typeof(int))
        {
            int v = (int)value!;
            return g.DragInt(label, ref v) && p.TrySet(v);
        }
        if (type == typeof(bool))
        {
            bool v = (bool)value!;
            return g.Toggle(label, ref v) && p.TrySet(v);
        }
        if (type.IsEnum)
        {
            var names = Enum.GetNames(type);
            int index = Array.IndexOf(names, value!.ToString());
            return g.Combo(label, ref index, names) && p.TrySet(names[index]);
        }
        if (type == typeof(Vector2))
        {
            var v = (Vector2)value!;
            return g.DragVector2(label, ref v, 0.05f) && p.TrySet(v);
        }
        if (type == typeof(Vector3))
        {
            var v = (Vector3)value!;
            return g.DragVector3(label, ref v, 0.05f) && p.TrySet(v);
        }
        if (type == typeof(Vector4))
        {
            var v = (Vector4)value!;
            return g.DragVector4(label, ref v, 0.05f) && p.TrySet(v);
        }
        if (type == typeof(Color4))
        {
            var c = (Color4)value!;
            var v = new Vector4(c.R, c.G, c.B, c.A);
            return g.ColorEdit(label, ref v) && p.TrySet(new Color4(v.X, v.Y, v.Z, v.W));
        }
        return false;
    }

    /// <summary>"DegreesPerSecond" becomes "Degrees Per Second".</summary>
    private static string Nicify(string name)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) sb.Append(' ');
            sb.Append(name[i]);
        }
        return sb.ToString();
    }
}
