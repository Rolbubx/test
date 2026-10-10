using System.Reflection;
using System.Text;
using System.Xml.Linq;
using GT67.Core;
using GT67.Scripting;
using GT67.UI;

namespace GT67.Editor;

/// <summary>One line of the API reference: a property, method or field of a scripting type, with its description.</summary>
public sealed record ApiMember(string Signature, string Summary);

/// <summary>One type of the scripting API with its members.</summary>
public sealed record ApiType(string Name, string Summary, IReadOnlyList<ApiMember> Members);

/// <summary>
/// The C# scripting API as a searchable list for the editor: the types a script uses (<c>Script</c>, <c>Game</c>,
/// <c>GameObject</c>, <c>Scene</c>, <c>Sound</c>, <c>Rain</c>...), their members and the descriptions from the
/// source's XML comments (GT67.xml, written next to the executable by the build).
/// </summary>
public sealed class ApiReference
{
    /// <summary>The types shown, in the order a new scripter needs them.</summary>
    private static readonly Type[] Types =
    {
        typeof(Script), typeof(Game), typeof(GameObject), typeof(Scene), typeof(GameTimer), typeof(RaycastHit),
        typeof(Sound), typeof(Rain), typeof(Sun), typeof(PointLight), typeof(CloudLayer), typeof(Camera), typeof(Water),
        typeof(Material), typeof(UiCanvas), typeof(UiElement),
    };

    private readonly List<ApiType> _types = new();

    public ApiReference()
    {
        var docs = LoadDocs();
        foreach (var type in Types) _types.Add(Describe(type, docs));
    }

    public IReadOnlyList<ApiType> All => _types;

    /// <summary>Types that have a member (or a name, or a description) containing <paramref name="filter"/>, with only the matching members.</summary>
    public IEnumerable<ApiType> Search(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            foreach (var type in _types) yield return type;
            yield break;
        }

        foreach (var type in _types)
        {
            bool typeMatches = Contains(type.Name, filter) || Contains(type.Summary, filter);
            var members = type.Members.Where(m => typeMatches || Contains(m.Signature, filter) || Contains(m.Summary, filter)).ToList();
            if (members.Count > 0) yield return type with { Members = members };
        }
    }

    private static bool Contains(string text, string filter) => text.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);

    // ---- reflection + xml docs -------------------------------------------------------------------

    private static ApiType Describe(Type type, Dictionary<string, string> docs)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var members = new List<ApiMember>();

        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            members.Add(new ApiMember($"new {type.Name}({Parameters(constructor)})", Doc(docs, "M:" + type.FullName + ".#ctor")));

        foreach (var property in type.GetProperties(flags).OrderBy(p => p.Name))
        {
            if (property.GetIndexParameters().Length > 0) continue;
            bool set = property.GetSetMethod() != null;
            string stat = (property.GetGetMethod() ?? property.GetSetMethod())!.IsStatic ? "static " : "";
            members.Add(new ApiMember($"{stat}{TypeName(property.PropertyType)} {property.Name} {{ get;{(set ? " set;" : "")} }}",
                Doc(docs, "P:" + type.FullName + "." + property.Name)));
        }

        foreach (var field in type.GetFields(flags).OrderBy(f => f.Name))
            members.Add(new ApiMember($"{(field.IsStatic ? "static " : "")}{TypeName(field.FieldType)} {field.Name}",
                Doc(docs, "F:" + type.FullName + "." + field.Name)));

        foreach (var method in type.GetMethods(flags).Where(m => !m.IsSpecialName).OrderBy(m => m.Name))
            members.Add(new ApiMember($"{(method.IsStatic ? "static " : "")}{TypeName(method.ReturnType)} {method.Name}({Parameters(method)})",
                Doc(docs, "M:" + type.FullName + "." + method.Name)));

        return new ApiType(type.Name, Doc(docs, "T:" + type.FullName), members);
    }

    private static string Parameters(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(p =>
            $"{TypeName(p.ParameterType)} {p.Name}" + (p.HasDefaultValue ? $" = {DefaultValue(p.DefaultValue)}" : "")));

    private static string DefaultValue(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture) + "f",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };

    private static string TypeName(Type type)
    {
        if (type == typeof(void)) return "void";
        if (type == typeof(float)) return "float";
        if (type == typeof(int)) return "int";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(string)) return "string";
        if (type == typeof(double)) return "double";
        if (type == typeof(object)) return "object";
        if (type.IsByRef) return TypeName(type.GetElementType()!);
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null) return TypeName(underlying) + "?";
        if (type.IsArray) return TypeName(type.GetElementType()!) + "[]";
        if (!type.IsGenericType) return type.Name;

        var sb = new StringBuilder(type.Name[..type.Name.IndexOf('`')]);
        sb.Append('<').Append(string.Join(", ", type.GetGenericArguments().Select(TypeName))).Append('>');
        return sb.ToString();
    }

    private static string Doc(Dictionary<string, string> docs, string key) => docs.TryGetValue(key, out var text) ? text : "";

    /// <summary>
    /// Reads GT67.xml. Methods are keyed without their parameter list, so every overload shows the first description
    /// found for that name.
    /// </summary>
    private static Dictionary<string, string> LoadDocs()
    {
        var docs = new Dictionary<string, string>();
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "GT67.xml");
            if (!File.Exists(path)) return docs;

            foreach (var member in XDocument.Load(path).Descendants("member"))
            {
                string? name = member.Attribute("name")?.Value;
                string? summary = member.Element("summary")?.Value;
                if (name == null || summary == null) continue;

                int paren = name.IndexOf('(');
                string key = paren >= 0 ? name[..paren] : name;
                string text = string.Join(' ', summary.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                docs.TryAdd(key, text);
            }
        }
        catch (Exception e) when (e is IOException or System.Xml.XmlException)
        {
            Utils.Log.Warn($"API descriptions not loaded: {e.Message}");
        }
        return docs;
    }
}
