using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using OpenTK.Mathematics;

namespace SolciteEngine.Player.Data;

/// <summary>
/// Game saves: one JSON file per name in the player's save folder (<see cref="Folder"/>). Save any of your own
/// classes or structs (their public fields and get/set properties), numbers, strings, bools, enums, Vector2/3/4,
/// Quaternion, Color4, arrays, lists and dictionaries. Works the same in the editor, in the game and in the
/// compiled game. Scripts call these directly (<c>DataSave(data, "slot1")</c>); other code uses <c>SaveData.DataSave</c>.
/// </summary>
/// <example>
/// <code>
/// public class Progress { public int Level = 1; public Vector3 Position; public List&lt;string&gt; Items = new(); }
///
/// DataSave(new Progress { Level = 3 }, "slot1");             // creates (or replaces) the save "slot1"
/// var p = DataLoad&lt;Progress&gt;("slot1");                     // reads it back
/// DataUpdate("slot1", p);                                    // replaces an existing save (error if there is none)
/// DataDelete("slot1");                                       // removes it
/// </code>
/// </example>
public static class SaveData
{
    private const string Extension = ".save.json";
    private const int FormatVersion = 1;
    private const int MaxDepth = 64;
    private static readonly object Gate = new();
    private static string _gameName = "SolciteEngine";
    private static string? _folder;

    /// <summary>
    /// The game's name, which picks its save folder. The editor sets it to the project's name and the compiled game
    /// to the name it was compiled from, so the editor and the game share their saves.
    /// </summary>
    public static string GameName
    {
        get => _gameName;
        set => _gameName = string.IsNullOrWhiteSpace(value) ? "SolciteEngine" : value.Trim();
    }

    /// <summary>
    /// Where the saves are written: <c>%LocalAppData%/SolciteEngine/Saves/&lt;GameName&gt;</c> on Windows,
    /// <c>~/.local/share/SolciteEngine/Saves/&lt;GameName&gt;</c> on Linux. Set it to use another folder.
    /// </summary>
    public static string Folder
    {
        get => _folder ?? Path.Combine(UserDataRoot(), "SolciteEngine", "Saves", EscapeName(GameName));
        set => _folder = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    // ---- the API ---------------------------------------------------------------------------------

    /// <summary>Saves <paramref name="data"/> under <paramref name="name"/>. A save that already has that name is replaced.</summary>
    public static void DataSave<T>(T data, string name) => Write(name, data, typeof(T), mustExist: false);

    /// <summary>
    /// Replaces the existing save <paramref name="name"/> with <paramref name="data"/>.
    /// Throws a <see cref="SaveDataException"/> if there is no save with that name (use <see cref="DataSave{T}"/> to create it).
    /// </summary>
    public static void DataUpdate<T>(string name, T data) => Write(name, data, typeof(T), mustExist: true);

    /// <summary>Deletes the save <paramref name="name"/>. Returns false if there was none (that is not an error).</summary>
    public static bool DataDelete(string name)
    {
        string path = PathOf(name);
        lock (Gate)
        {
            try
            {
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new SaveDataException($"Could not delete the save '{name}': {e.Message}", e);
            }
        }
    }

    /// <summary>Reads the save <paramref name="name"/>. Throws a <see cref="SaveDataException"/> if there is none.</summary>
    public static T DataLoad<T>(string name)
    {
        if (!TryRead(name, out T value))
            throw new SaveDataException($"There is no save named '{name}' (check with DataExists first, or use DataLoad(name, defaultValue)).");
        return value;
    }

    /// <summary>Reads the save <paramref name="name"/>, or returns <paramref name="defaultValue"/> if there is none.</summary>
    public static T DataLoad<T>(string name, T defaultValue) => TryRead(name, out T value) ? value : defaultValue;

    /// <summary>True if there is a save named <paramref name="name"/>.</summary>
    public static bool DataExists(string name) => File.Exists(PathOf(name));

    /// <summary>The names of every save, most recent first.</summary>
    public static string[] DataList()
    {
        string folder = Folder;
        if (!Directory.Exists(folder)) return Array.Empty<string>();
        return new DirectoryInfo(folder).EnumerateFiles("*" + Extension)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => UnescapeName(f.Name[..^Extension.Length]))
            .ToArray();
    }

    /// <summary>When the save <paramref name="name"/> was last written (local time), or null if there is none.</summary>
    public static DateTime? DataSavedAt(string name)
    {
        string path = PathOf(name);
        return File.Exists(path) ? File.GetLastWriteTime(path) : null;
    }

    // ---- files -----------------------------------------------------------------------------------

    private static void Write(string name, object? data, Type type, bool mustExist)
    {
        string path = PathOf(name);
        byte[] bytes;
        try
        {
            bytes = Serialize(data, type);
        }
        catch (SaveDataException e)
        {
            throw new SaveDataException($"Could not save '{name}': {e.Message}");
        }

        lock (Gate)
        {
            if (mustExist && !File.Exists(path))
                throw new SaveDataException($"DataUpdate: there is no save named '{name}' to update (use DataSave to create it).");
            string temp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, path, overwrite: true);   // never leaves a half-written save behind
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                try { File.Delete(temp); } catch (Exception) { /* best effort */ }
                throw new SaveDataException($"Could not write the save '{name}': {e.Message}", e);
            }
        }
    }

    private static bool TryRead<T>(string name, out T value)
    {
        string path = PathOf(name);
        byte[] bytes;
        lock (Gate)
        {
            try
            {
                if (!File.Exists(path)) { value = default!; return false; }
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new SaveDataException($"Could not read the save '{name}': {e.Message}", e);
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(bytes);
            var data = doc.RootElement.TryGetProperty("data", out var d) ? d : doc.RootElement;
            value = (T)Read(data, typeof(T), 0, NewInstance<T>())!;
            return true;
        }
        catch (Exception e) when (e is JsonException or SaveDataException or FormatException or InvalidCastException or OverflowException or InvalidOperationException)
        {
            throw new SaveDataException($"The save '{name}' could not be read as {Pretty(typeof(T))}: {e.Message}", e);
        }
    }

    private static string PathOf(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new SaveDataException("A save needs a name (DataSave(data, \"slot1\")).");
        if (name.Length > 120) throw new SaveDataException($"The save name '{name[..20]}...' is too long (120 characters at most).");
        return Path.Combine(Folder, EscapeName(name) + Extension);
    }

    private static string UserDataRoot()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(root))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            root = Path.Combine(string.IsNullOrEmpty(home) ? Path.GetTempPath() : home, ".local", "share");
        }
        return root;
    }

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Any name becomes a safe file name: letters, digits, space, '-' and '_' stay, the rest becomes %XX (UTF-8).</summary>
    private static string EscapeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (byte b in Encoding.UTF8.GetBytes(name))
        {
            char c = (char)b;
            bool keep = b < 0x80 && (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' || (c == ' ' && sb.Length > 0));
            if (keep) sb.Append(c);
            else sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        if (sb[^1] == ' ') { sb.Length--; sb.Append("%20"); }         // Windows drops trailing spaces
        if (ReservedNames.Contains(sb.ToString()))                      // "CON" is a device on Windows
            sb.Remove(0, 1).Insert(0, "%" + ((byte)name[0]).ToString("X2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private static string UnescapeName(string file)
    {
        var bytes = new List<byte>(file.Length);
        for (int i = 0; i < file.Length; i++)
        {
            if (file[i] == '%' && i + 2 < file.Length &&
                byte.TryParse(file.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            {
                bytes.Add(b);
                i += 2;
            }
            else bytes.AddRange(Encoding.UTF8.GetBytes(file[i].ToString()));
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    // ---- writing JSON ----------------------------------------------------------------------------
    // Hand-written (reflection over public fields/properties) rather than JsonSerializer: reflection-based
    // System.Text.Json is off in the Native AOT game, and the engine cannot know the game's own types at build time.

    private static byte[] Serialize(object? data, Type type)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", FormatVersion);
            w.WriteString("savedAt", DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
            w.WritePropertyName("data");
            WriteValue(w, data, type, 0);
            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void WriteValue(Utf8JsonWriter w, object? value, Type declared, int depth)
    {
        if (depth > MaxDepth) throw new SaveDataException("the data is nested too deeply (does it contain a loop?).");
        CheckSavable(Nullable.GetUnderlyingType(declared) ?? declared);   // refuse a GameObject field even while it is null
        if (value is null) { w.WriteNullValue(); return; }
        var type = value.GetType();

        switch (value)
        {
            case string s: w.WriteStringValue(s); return;
            case bool b: w.WriteBooleanValue(b); return;
            case char c: w.WriteStringValue(c.ToString()); return;
            case Enum e: w.WriteStringValue(e.ToString()); return;
            case float f: if (float.IsFinite(f)) w.WriteNumberValue(f); else w.WriteStringValue(f.ToString(CultureInfo.InvariantCulture)); return;
            case double d: if (double.IsFinite(d)) w.WriteNumberValue(d); else w.WriteStringValue(d.ToString(CultureInfo.InvariantCulture)); return;
            case decimal m: w.WriteNumberValue(m); return;
            case int i: w.WriteNumberValue(i); return;
            case long l: w.WriteNumberValue(l); return;
            case short sh: w.WriteNumberValue(sh); return;
            case byte by: w.WriteNumberValue(by); return;
            case sbyte sb: w.WriteNumberValue(sb); return;
            case uint ui: w.WriteNumberValue(ui); return;
            case ulong ul: w.WriteNumberValue(ul); return;
            case ushort us: w.WriteNumberValue(us); return;
            case DateTime dt: w.WriteStringValue(dt.ToString("o", CultureInfo.InvariantCulture)); return;
            case TimeSpan ts: w.WriteStringValue(ts.ToString("c", CultureInfo.InvariantCulture)); return;
            case Guid g: w.WriteStringValue(g.ToString()); return;
        }
        if (MathToFloats(value) is { } math) { WriteMath(w, math.names, math.values); return; }

        CheckSavable(type);

        if (value is IDictionary dict)
        {
            w.WriteStartObject();
            var valueType = DictionaryValueType(type);
            foreach (DictionaryEntry entry in dict)
            {
                w.WritePropertyName(Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "");
                WriteValue(w, entry.Value, valueType, depth + 1);
            }
            w.WriteEndObject();
            return;
        }

        if (value is IEnumerable list)
        {
            var itemType = ItemType(type);
            w.WriteStartArray();
            foreach (var item in list) WriteValue(w, item, itemType, depth + 1);
            w.WriteEndArray();
            return;
        }

        w.WriteStartObject();
        foreach (var member in Members(type))
        {
            w.WritePropertyName(member.Name);
            WriteValue(w, member.Get(value), member.Type, depth + 1);
        }
        w.WriteEndObject();
    }

    // ---- reading JSON ----------------------------------------------------------------------------

    /// <summary>
    /// A fresh T to fill, made with the generic Activator so the Native AOT game keeps its constructor (the
    /// reflection-based Activator.CreateInstance(Type) cannot build List&lt;string&gt; there). Null for types without one.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2091", Justification = "The game and the engine are trimmer roots, so save classes keep their members; the Native AOT game is handled (OpenTK maths by hand, collections reuse the field's instance, a clear error otherwise).")]
    private static object? NewInstance<T>()
    {
        var type = typeof(T);
        if (type.IsPrimitive || type.IsEnum || type.IsArray || type.IsInterface || type.IsAbstract || type == typeof(string)) return null;
        try { return Activator.CreateInstance<T>(); }
        catch (MissingMethodException) { return null; }
    }

    /// <param name="existing">An instance to fill instead of creating one (the default value of a field: <c>Items = new()</c>).</param>
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "The game and the engine are trimmer roots, so save classes keep their members; the Native AOT game is handled (OpenTK maths by hand, collections reuse the field's instance, a clear error otherwise).")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The game and the engine are trimmer roots, so save classes keep their members; the Native AOT game is handled (OpenTK maths by hand, collections reuse the field's instance, a clear error otherwise).")]
    private static object? Read(JsonElement e, Type type, int depth, object? existing = null)
    {
        if (depth > MaxDepth) throw new SaveDataException("the save is nested too deeply.");
        if (e.ValueKind == JsonValueKind.Null)
        {
            if (type.IsValueType && Nullable.GetUnderlyingType(type) == null) return Activator.CreateInstance(type);
            return null;
        }
        if (Nullable.GetUnderlyingType(type) is { } inner) type = inner;

        if (type == typeof(object)) return ReadLoose(e, depth);
        if (type == typeof(string)) return e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText();
        if (type == typeof(bool)) return e.GetBoolean();
        if (type == typeof(char)) { string s = e.GetString() ?? ""; return s.Length > 0 ? s[0] : '\0'; }
        if (type.IsEnum)
            return e.ValueKind == JsonValueKind.Number ? Enum.ToObject(type, e.GetInt64()) : Enum.Parse(type, e.GetString()!, ignoreCase: true);
        if (type == typeof(float)) return e.ValueKind == JsonValueKind.String ? float.Parse(e.GetString()!, CultureInfo.InvariantCulture) : e.GetSingle();
        if (type == typeof(double)) return e.ValueKind == JsonValueKind.String ? double.Parse(e.GetString()!, CultureInfo.InvariantCulture) : e.GetDouble();
        if (type == typeof(decimal)) return e.GetDecimal();
        if (type == typeof(int)) return e.GetInt32();
        if (type == typeof(long)) return e.GetInt64();
        if (type == typeof(short)) return e.GetInt16();
        if (type == typeof(byte)) return e.GetByte();
        if (type == typeof(sbyte)) return e.GetSByte();
        if (type == typeof(uint)) return e.GetUInt32();
        if (type == typeof(ulong)) return e.GetUInt64();
        if (type == typeof(ushort)) return e.GetUInt16();
        if (type == typeof(DateTime)) return DateTime.Parse(e.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (type == typeof(TimeSpan)) return TimeSpan.Parse(e.GetString()!, CultureInfo.InvariantCulture);
        if (type == typeof(Guid)) return Guid.Parse(e.GetString()!);
        if (ReadMath(e, type) is { } math) return math;

        CheckSavable(type);

        if (type.IsArray)
        {
            var itemType = type.GetElementType()!;
            var array = Array.CreateInstance(itemType, e.GetArrayLength());
            int i = 0;
            foreach (var item in e.EnumerateArray()) array.SetValue(Read(item, itemType, depth + 1), i++);
            return array;
        }

        if (type.IsInterface || type.IsAbstract)
            throw new SaveDataException($"{Pretty(type)} is an interface or an abstract class: use a concrete type such as List<T> or Dictionary<string, T>.");

        object target = existing != null && type.IsInstanceOfType(existing) ? existing : Create(type);
        if (target is IDictionary existingDict && ReferenceEquals(target, existing)) existingDict.Clear();
        else if (target is IList existingList && ReferenceEquals(target, existing)) existingList.Clear();

        if (target is IDictionary dict)
        {
            var keyType = type.IsGenericType ? type.GetGenericArguments()[0] : typeof(string);
            var valueType = DictionaryValueType(type);
            foreach (var p in e.EnumerateObject())
                dict[ParseKey(p.Name, keyType)] = Read(p.Value, valueType, depth + 1);
            return dict;
        }

        if (target is IList list)
        {
            var itemType = ItemType(type);
            foreach (var item in e.EnumerateArray()) list.Add(Read(item, itemType, depth + 1));
            return list;
        }

        if (e.ValueKind != JsonValueKind.Object)
            throw new SaveDataException($"expected an object for {Pretty(type)}, found {e.ValueKind}.");
        foreach (var member in Members(type))
            if (e.TryGetProperty(member.Name, out var field))   // members added since the save keep their default
                member.Set(target, Read(field, member.Type, depth + 1, member.Type.IsValueType ? null : member.Get(target)));
        return target;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "The game and the engine are trimmer roots, so save classes keep their members; the Native AOT game is handled (OpenTK maths by hand, collections reuse the field's instance, a clear error otherwise).")]
    private static object Create(Type type)
    {
        try
        {
            return Activator.CreateInstance(type) ?? throw new SaveDataException($"{Pretty(type)} cannot be created.");
        }
        catch (MissingMethodException)
        {
            // Native AOT game: a List<T>/Dictionary<K, V> nested in another collection, or a field left null.
            throw new SaveDataException($"{Pretty(type)} cannot be created here: give the field a starting value " +
                                        "(public List<string> Items = new();) and avoid collections inside collections.");
        }
    }

    /// <summary>A value saved into an <c>object</c> field comes back as double, string, bool, List&lt;object?&gt; or Dictionary&lt;string, object?&gt;.</summary>
    private static object? ReadLoose(JsonElement e, int depth) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => e.EnumerateArray().Select(x => ReadLoose(x, depth + 1)).ToList(),
        JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => ReadLoose(p.Value, depth + 1)),
        _ => null,
    };

    private static object ParseKey(string key, Type keyType)
    {
        if (keyType == typeof(string) || keyType == typeof(object)) return key;
        if (keyType.IsEnum) return Enum.Parse(keyType, key, ignoreCase: true);
        return Convert.ChangeType(key, keyType, CultureInfo.InvariantCulture);
    }

    // ---- OpenTK maths ----------------------------------------------------------------------------
    // Written by hand: the trimmed / Native AOT game drops the reflection data of OpenTK's fields (X, Y, Z...).

    private static (string names, float[] values)? MathToFloats(object value) => value switch
    {
        Vector2 v => ("XY", new[] { v.X, v.Y }),
        Vector3 v => ("XYZ", new[] { v.X, v.Y, v.Z }),
        Vector4 v => ("XYZW", new[] { v.X, v.Y, v.Z, v.W }),
        Quaternion q => ("XYZW", new[] { q.X, q.Y, q.Z, q.W }),
        Color4 c => ("RGBA", new[] { c.R, c.G, c.B, c.A }),
        Vector2i v => ("XY", new float[] { v.X, v.Y }),
        Vector3i v => ("XYZ", new float[] { v.X, v.Y, v.Z }),
        Vector4i v => ("XYZW", new float[] { v.X, v.Y, v.Z, v.W }),
        _ => null,
    };

    private static void WriteMath(Utf8JsonWriter w, string names, float[] values)
    {
        w.WriteStartObject();
        for (int i = 0; i < values.Length; i++)
        {
            w.WritePropertyName(names[i].ToString());
            if (float.IsFinite(values[i])) w.WriteNumberValue(values[i]);
            else w.WriteStringValue(values[i].ToString(CultureInfo.InvariantCulture));
        }
        w.WriteEndObject();
    }

    private static object? ReadMath(JsonElement e, Type type)
    {
        float C(string n, float fallback = 0f)
        {
            if (!e.TryGetProperty(n, out var v)) return fallback;
            return v.ValueKind == JsonValueKind.String ? float.Parse(v.GetString()!, CultureInfo.InvariantCulture) : v.GetSingle();
        }
        int I(string n) => (int)MathF.Round(C(n));
        if (type == typeof(Vector2)) return new Vector2(C("X"), C("Y"));
        if (type == typeof(Vector3)) return new Vector3(C("X"), C("Y"), C("Z"));
        if (type == typeof(Vector4)) return new Vector4(C("X"), C("Y"), C("Z"), C("W"));
        if (type == typeof(Quaternion)) return new Quaternion(C("X"), C("Y"), C("Z"), C("W", 1f));
        if (type == typeof(Color4)) return new Color4(C("R"), C("G"), C("B"), C("A", 1f));
        if (type == typeof(Vector2i)) return new Vector2i(I("X"), I("Y"));
        if (type == typeof(Vector3i)) return new Vector3i(I("X"), I("Y"), I("Z"));
        if (type == typeof(Vector4i)) return new Vector4i(I("X"), I("Y"), I("Z"), I("W"));
        return null;
    }

    // ---- types -----------------------------------------------------------------------------------

    private sealed record Member(string Name, Type Type, Func<object, object?> Get, Action<object, object?> Set);

    private static readonly Dictionary<Type, Member[]> MemberCache = new();

    /// <summary>
    /// What is saved of a class or struct: its public fields and its public get/set properties. OpenTK's maths types
    /// (Vector3, Quaternion, Color4...) only keep their fields (X, Y, Z...), not their swizzles (Xy, Zyx...).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "The game and the engine are trimmer roots, so save classes keep their members; the Native AOT game is handled (OpenTK maths by hand, collections reuse the field's instance, a clear error otherwise).")]
    private static Member[] Members(Type type)
    {
        lock (MemberCache)
        {
            if (MemberCache.TryGetValue(type, out var cached)) return cached;
            var members = new List<Member>();
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.IsInitOnly || f.IsDefined(typeof(NonSerializedAttribute)) || Ignored(f)) continue;
                members.Add(new Member(f.Name, f.FieldType, f.GetValue, f.SetValue));
            }
            bool fieldsOnly = type.Namespace?.StartsWith("OpenTK", StringComparison.Ordinal) == true;
            if (!fieldsOnly)
                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.GetIndexParameters().Length > 0 || p.GetMethod?.IsPublic != true || p.SetMethod?.IsPublic != true || Ignored(p)) continue;
                    members.Add(new Member(p.Name, p.PropertyType, p.GetValue, p.SetValue));
                }
            return MemberCache[type] = members.ToArray();
        }
    }

    private static bool Ignored(MemberInfo m) =>
        m.IsDefined(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute)) || m.IsDefined(typeof(SaveIgnoreAttribute));

    /// <summary>Engine objects (GameObject, Script, Scene, Sound...) live in the running game, not in a save file.</summary>
    private static void CheckSavable(Type type)
    {
        if (typeof(Delegate).IsAssignableFrom(type))
            throw new SaveDataException($"{Pretty(type)} is a function (delegate) and cannot be saved.");
        if (!type.IsValueType && type.Assembly == typeof(SaveData).Assembly)
            throw new SaveDataException(
                $"{Pretty(type)} is an object of the running game and cannot be saved: save what you need from it instead " +
                "(its Position, its Name, a number...) and rebuild it when you load.");
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "The game and the engine are trimmer roots, so save classes keep their members; the Native AOT game is handled (OpenTK maths by hand, collections reuse the field's instance, a clear error otherwise).")]
    private static Type ItemType(Type type)
    {
        if (type.IsArray) return type.GetElementType()!;
        foreach (var i in type.GetInterfaces().Prepend(type))
            if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return i.GetGenericArguments()[0];
        return typeof(object);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "The game and the engine are trimmer roots, so save classes keep their members; the Native AOT game is handled (OpenTK maths by hand, collections reuse the field's instance, a clear error otherwise).")]
    private static Type DictionaryValueType(Type type)
    {
        foreach (var i in type.GetInterfaces().Prepend(type))
            if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>)) return i.GetGenericArguments()[1];
        return typeof(object);
    }

    private static string Pretty(Type t) =>
        t.IsGenericType ? $"{t.Name[..t.Name.IndexOf('`')]}<{string.Join(", ", t.GetGenericArguments().Select(Pretty))}>" : t.Name;
}

/// <summary>A save could not be written, found or read. The message says which save and why.</summary>
public sealed class SaveDataException : Exception
{
    public SaveDataException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Put on a public field or property that must not be saved (DataSave skips it, DataLoad leaves it as it is).</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class SaveIgnoreAttribute : Attribute { }
