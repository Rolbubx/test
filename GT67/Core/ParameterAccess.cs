using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>One named, settable value of an object (a "parameter"): a public property or field, found by reflection.</summary>
public sealed class ParameterDescriptor
{
    private readonly object _owner;
    private readonly MemberInfo _member;

    internal ParameterDescriptor(object owner, MemberInfo member)
    {
        _owner = owner;
        _member = member;
    }

    public string Name => _member.Name;

    public Type Type => _member is PropertyInfo p ? p.PropertyType : ((FieldInfo)_member).FieldType;

    /// <summary>The current value (boxed).</summary>
    public object? Value => _member is PropertyInfo p ? p.GetValue(_owner) : ((FieldInfo)_member).GetValue(_owner);

    /// <summary>Sets the value, converting numbers, tuples, arrays and enum names where needed.</summary>
    public bool TrySet(object? value)
    {
        if (!ParameterAccess.TryConvert(value, Type, out var converted)) return false;
        if (_member is PropertyInfo p) p.SetValue(_owner, converted);
        else ((FieldInfo)_member).SetValue(_owner, converted);
        return true;
    }

    public override string ToString() => $"{Name}: {Type.Name} = {Value}";
}

/// <summary>
/// Reads and writes the public settings of an object by name, so scripts can tweak effects without referring to
/// their types: <c>ParameterAccess.TrySet(effect, "Intensity", 0.8f)</c>. Works on public read/write properties and
/// non-readonly fields of these types: float, int, bool, enums, Vector2/3/4 and Color4 (settable from tuples such as
/// <c>(1f, 0.5f, 0.2f)</c> or arrays).
/// </summary>
public static class ParameterAccess
{
    private static readonly Dictionary<Type, MemberInfo[]> Cache = new();

    /// <summary>Every public settable property or field of a supported type.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "GT67 is a trimmer root (GT67.csproj), so scripts, effects and materials keep their members.")]
    public static IReadOnlyList<ParameterDescriptor> Describe(object target)
    {
        var type = target.GetType();
        lock (Cache)
        {
            if (!Cache.TryGetValue(type, out var members))
            {
                var found = new List<MemberInfo>();
                found.AddRange(type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead && p.GetGetMethod() != null && p.GetSetMethod() != null && p.GetIndexParameters().Length == 0 && IsSupported(p.PropertyType)));
                found.AddRange(type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Where(f => !f.IsInitOnly && !f.IsLiteral && IsSupported(f.FieldType)));

                members = found
                    .OrderBy(m => m.DeclaringType == type ? 0 : 1) // the type's own settings first
                    .ThenBy(m => m.Name)
                    .ToArray();
                Cache[type] = members;
            }
            return members.Select(m => new ParameterDescriptor(target, m)).ToList();
        }
    }

    /// <summary>Finds a parameter by (case-insensitive) name, or null.</summary>
    public static ParameterDescriptor? Find(object target, string name) =>
        Describe(target).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public static object? Get(object target, string name) => Find(target, name)?.Value;

    public static bool TrySet(object target, string name, object? value) => Find(target, name)?.TrySet(value) ?? false;

    /// <summary>Copies every parameter value from one object to another of the same (or a compatible) type.</summary>
    public static void CopyValues(object from, object to)
    {
        foreach (var source in Describe(from))
            Find(to, source.Name)?.TrySet(source.Value);
    }

    public static bool IsSupported(Type t) =>
        t == typeof(float) || t == typeof(int) || t == typeof(bool) || t.IsEnum ||
        t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4) || t == typeof(Color4);

    // ---- conversion ------------------------------------------------------------------------------

    internal static bool TryConvert(object? value, Type target, out object? result)
    {
        result = null;
        if (value == null) return false;

        if (target.IsInstanceOfType(value))
        {
            result = value;
            return true;
        }

        if (target == typeof(float) && IsNumber(value)) return Done(Convert.ToSingle(value, CultureInfo.InvariantCulture), out result);
        if (target == typeof(int) && IsNumber(value)) return Done((int)MathF.Round(Convert.ToSingle(value, CultureInfo.InvariantCulture)), out result);
        if (target == typeof(bool))
        {
            if (value is string s && bool.TryParse(s, out bool parsed)) return Done(parsed, out result);
            if (IsNumber(value)) return Done(Convert.ToDouble(value, CultureInfo.InvariantCulture) != 0.0, out result);
            return false;
        }
        if (target.IsEnum)
        {
            if (value is string name && Enum.TryParse(target, name, ignoreCase: true, out var e)) return Done(e!, out result);
            if (IsNumber(value)) return Done(Enum.ToObject(target, Convert.ToInt32(value, CultureInfo.InvariantCulture)), out result);
            return false;
        }

        if (!TryGetFloats(value, out var f)) return false;
        if (target == typeof(Vector2) && f.Length == 2) return Done(new Vector2(f[0], f[1]), out result);
        if (target == typeof(Vector3) && f.Length == 3) return Done(new Vector3(f[0], f[1], f[2]), out result);
        if (target == typeof(Vector4) && f.Length == 4) return Done(new Vector4(f[0], f[1], f[2], f[3]), out result);
        if (target == typeof(Color4) && f.Length is 3 or 4)
            return Done(new Color4(f[0], f[1], f[2], f.Length == 4 ? f[3] : 1f), out result);
        return false;
    }

    private static bool Done(object value, out object? result)
    {
        result = value;
        return true;
    }

    private static bool IsNumber(object v) =>
        v is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

    /// <summary>Reads (r, g, b)-style tuples, arrays and Vector/Color values as a float list.</summary>
    private static bool TryGetFloats(object value, out float[] floats)
    {
        floats = Array.Empty<float>();
        switch (value)
        {
            case Vector2 v: floats = new[] { v.X, v.Y }; return true;
            case Vector3 v: floats = new[] { v.X, v.Y, v.Z }; return true;
            case Vector4 v: floats = new[] { v.X, v.Y, v.Z, v.W }; return true;
            case Color4 c: floats = new[] { c.R, c.G, c.B, c.A }; return true;
            case ITuple t:
            {
                var list = new float[t.Length];
                for (int i = 0; i < t.Length; i++)
                {
                    if (t[i] == null || !IsNumber(t[i]!)) return false;
                    list[i] = Convert.ToSingle(t[i], CultureInfo.InvariantCulture);
                }
                floats = list;
                return true;
            }
            case Array a:
            {
                var list = new float[a.Length];
                for (int i = 0; i < a.Length; i++)
                {
                    var item = a.GetValue(i);
                    if (item == null || !IsNumber(item)) return false;
                    list[i] = Convert.ToSingle(item, CultureInfo.InvariantCulture);
                }
                floats = list;
                return true;
            }
            default:
                return false;
        }
    }
}
