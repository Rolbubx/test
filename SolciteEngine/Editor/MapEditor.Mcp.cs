using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SolciteEngine.Core;
using SolciteEngine.Scripting;
using SolciteEngine.Utils;
using OpenTK.Mathematics;

namespace SolciteEngine.Editor;

// The MCP tools: execute_script, create_object, delete_object, get_tree, attach_component. They run on the editor's
// thread (McpServer.Pump, called from Update), so they can touch the scene freely.
public sealed partial class MapEditor
{
    private McpServer? _mcp;
    private readonly ScriptCompiler _mcpCompiler = new();   // its own compiler: the project's scripts keep theirs

    private static readonly string[] ShapeNames = { "Cube", "Sphere", "Plane", "Cylinder", "Cone", "Capsule", "Torus" };

    /// <summary>Starts the MCP server on <paramref name="port"/> (see <c>--mcp</c>).</summary>
    public void StartMcp(int port)
    {
        try
        {
            _mcp = new McpServer(McpTools(), port);
            _mcp.Start();
            Say($"MCP server on http://127.0.0.1:{port}/mcp");
        }
        catch (Exception e)
        {
            _mcp?.Dispose();
            _mcp = null;
            Say($"MCP server could not start on port {port}: {e.Message}", error: true);
        }
    }

    private IEnumerable<McpTool> McpTools()
    {
        static JsonObject Schema(JsonObject properties, params string[] required) => new()
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray()),
        };
        static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };

        yield return new McpTool("execute_script",
            "Compiles and runs a C# snippet in the editor, on the open scene. The snippet is the body of a method: `scene` (Scene) and " +
            "`print(object)` are in scope, SolciteEngine.Core/Scripting/Utils and OpenTK.Mathematics are imported. " +
            "Returns what was printed plus the returned value, or the compiler errors.",
            Schema(new JsonObject { ["content"] = Str("C# statements, e.g. `scene.Find(\"Cube\").Y = 2; return scene.Roots.Count;`") }, "content"),
            ExecuteScript);

        yield return new McpTool("create_object",
            $"Adds an object to the open scene. type: {string.Join(", ", ShapeNames)}, PointLight or SpotLight.",
            Schema(new JsonObject
            {
                ["type"] = Str("Cube, Sphere, Plane, Cylinder, Cone, Capsule, Torus, PointLight or SpotLight"),
                ["name"] = Str("Name of the object (made unique if taken)"),
                ["properties"] = new JsonObject
                {
                    ["type"] = "object",
                    ["description"] = "Optional. Any public property of the object by name: position/rotation(Euler degrees, as [x,y,z]), " +
                        "scale ([x,y,z] or a number), color ([r,g,b(,a)] 0-1 or \"#rrggbb\"), Tag, Enabled, Visible, CastShadow, Opacity, Intensity, Range...",
                },
            }, "type", "name"),
            CreateObject);

        yield return new McpTool("delete_object",
            "Deletes an object (and its children), light or sound from the open scene. name is its name, or a path such as \"Parent/Child\".",
            Schema(new JsonObject { ["name"] = Str("Name or path of the object") }, "name"),
            DeleteObject);

        yield return new McpTool("get_tree",
            "Returns the open scene as JSON: every object (transform, scripts, children), light and sound, and the script types that can be attached.",
            Schema(new JsonObject()),
            _ => GetTree());

        yield return new McpTool("attach_component",
            "Attaches a component to an object: a script class from the project's Scripts folder, or a PointLight / SpotLight that follows the object.",
            Schema(new JsonObject
            {
                ["type"] = Str("Script class name (see get_tree > scriptTypes), PointLight or SpotLight"),
                ["path"] = Str("Name or path (\"Parent/Child\") of the target object"),
                ["where"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray("self", "children"),
                    ["description"] = "self (default): only the target object. children: the target and all its descendants.",
                },
            }, "type", "path"),
            AttachComponent);
    }

    // ---- execute_script --------------------------------------------------------------------------

    private string ExecuteScript(JsonElement args)
    {
        string content = Arg(args, "content");
        const string header =
            "using SolciteEngine.Core; using SolciteEngine.Scripting; using SolciteEngine.Utils; using OpenTK.Mathematics;\n" +
            "public static class __McpScript { public static object? Run(Scene scene, Action<object?> print) {\n#line 1 \"<script>\"\n";
        string source = header + content + "\n#line hidden\n return null; } }";

        var result = _mcpCompiler.Compile(new[] { ("<script>", source) });
        if (!result.Success) throw new InvalidOperationException("Compilation failed:\n" + string.Join("\n", result.Errors));

        var output = new StringBuilder();
        var run = result.Assembly!.GetType("__McpScript")!.GetMethod("Run")!;
        object? value;
        try
        {
            value = run.Invoke(null, new object[] { Scene, (Action<object?>)(o => output.AppendLine(o?.ToString())) });
        }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            throw new InvalidOperationException($"{e.InnerException.GetType().Name}: {e.InnerException.Message}" +
                (output.Length > 0 ? "\nOutput before the error:\n" + output : ""));
        }
        if (value != null) output.AppendLine("=> " + value);
        return output.Length > 0 ? output.ToString().TrimEnd() : "OK";
    }

    // ---- create_object ---------------------------------------------------------------------------

    private string CreateObject(JsonElement args)
    {
        string type = Arg(args, "type");
        string name = Arg(args, "name");
        args.TryGetProperty("properties", out var properties);
        if (Playing) throw new InvalidOperationException("Stop playing before changing the scene.");

        if (type.Equals("PointLight", StringComparison.OrdinalIgnoreCase))
        {
            var light = new PointLight(Vector3.Zero, Color4.White, 3f, 12f) { Name = UniqueLightName(name) };
            ApplyProperties(light, properties);
            Scene.AddLight(light);
            if (!Scene.PointLights.Contains(light)) throw new InvalidOperationException($"A scene can have {Scene.MaxPointLights} point lights.");
            return $"Created point light '{light.Name}'.";
        }
        if (type.Equals("SpotLight", StringComparison.OrdinalIgnoreCase))
        {
            if (Scene.SpotLights.Count >= Scene.MaxSpotLights) throw new InvalidOperationException($"A scene can have {Scene.MaxSpotLights} spot lights.");
            var light = new SpotLight(new Vector3(0f, 3f, 0f), -Vector3.UnitY) { Name = UniqueLightName(name), Intensity = 4f, Range = 25f };
            ApplyProperties(light, properties);
            Scene.AddLight(light);
            return $"Created spot light '{light.Name}'.";
        }

        Mesh mesh = ShapeNames.FirstOrDefault(s => s.Equals(type, StringComparison.OrdinalIgnoreCase)) switch
        {
            "Cube" => Utils.Primitives.Cube(1f),
            "Sphere" => Utils.Primitives.Sphere(0.5f, 32),
            "Plane" => Utils.Primitives.Plane(4f, 4f, 1),
            "Cylinder" => Utils.Primitives.Cylinder(0.5f, 1f, 32),
            "Cone" => Utils.Primitives.Cone(0.5f, 1f, 32),
            "Capsule" => Utils.Primitives.Capsule(0.35f, 1.5f, 24),
            "Torus" => Utils.Primitives.Torus(0.6f, 0.2f, 40, 20),
            _ => throw new ArgumentException($"Unknown type '{type}'. Use {string.Join(", ", ShapeNames)}, PointLight or SpotLight."),
        };
        var obj = new GameObject(mesh, Vector3.Zero) { Name = ModelImporter.UniqueName(Scene, name) };
        try { ApplyProperties(obj, properties); }
        catch { obj.Destroy(); throw; }
        Scene.Add(obj);
        return $"Created {type} '{obj.Name}' at {Fmt(obj.Position)}.";
    }

    private string UniqueLightName(string name)
    {
        string unique = name;
        for (int i = 2; Scene.PointLights.Any(l => l.Name == unique) || Scene.SpotLights.Any(l => l.Name == unique); i++) unique = $"{name} {i}";
        return unique;
    }

    // ---- delete_object ---------------------------------------------------------------------------

    private string DeleteObject(JsonElement args)
    {
        string name = Arg(args, "name");
        if (Playing) throw new InvalidOperationException("Stop playing before changing the scene.");

        if (FindObject(name) is { } obj)
        {
            if (Scene.Terrains.Any(t => t.Object == obj)) throw new InvalidOperationException("Terrains cannot be deleted from here.");
            if (Selected == obj) Selected = null;
            obj.Destroy();
            return $"Deleted '{obj.Name}'.";
        }
        if (Scene.PointLights.FirstOrDefault(l => l.Name == name) is { } point) { Scene.PointLights.Remove(point); return $"Deleted point light '{name}'."; }
        if (Scene.SpotLights.FirstOrDefault(l => l.Name == name) is { } spot) { Scene.SpotLights.Remove(spot); return $"Deleted spot light '{name}'."; }
        if (Scene.FindSound(name) is { } sound) { Scene.Remove(sound); return $"Deleted sound '{name}'."; }
        throw new ArgumentException($"Nothing named '{name}' in the scene (see get_tree).");
    }

    // ---- get_tree --------------------------------------------------------------------------------

    private string GetTree()
    {
        static JsonArray Vec(Vector3 v) => new(Math.Round(v.X, 3), Math.Round(v.Y, 3), Math.Round(v.Z, 3));

        JsonObject Node(GameObject o)
        {
            var node = new JsonObject
            {
                ["name"] = o.Name,
                ["position"] = Vec(o.Position),
                ["rotation"] = Vec(o.EulerAngles),
                ["scale"] = Vec(o.Scale),
                ["enabled"] = o.Enabled,
            };
            if (o.Tag.Length > 0) node["tag"] = o.Tag;
            if (o.Scripts.Count > 0) node["scripts"] = new JsonArray(o.Scripts.Select(s => (JsonNode)s.GetType().Name).ToArray());
            var children = o.Children.Where(c => !c.EditorOnly).Select(Node).ToArray();
            if (children.Length > 0) node["children"] = new JsonArray(children);
            return node;
        }

        var tree = new JsonObject
        {
            ["scene"] = Scene.Name,
            ["playing"] = Playing,
            ["objects"] = new JsonArray(Scene.Roots.Where(o => !o.EditorOnly).Select(o => (JsonNode)Node(o)).ToArray()),
            ["pointLights"] = new JsonArray(Scene.PointLights.Select(l => (JsonNode)new JsonObject { ["name"] = l.Name, ["position"] = Vec(l.Position) }).ToArray()),
            ["spotLights"] = new JsonArray(Scene.SpotLights.Select(l => (JsonNode)new JsonObject { ["name"] = l.Name, ["position"] = Vec(l.Position) }).ToArray()),
            ["scriptTypes"] = new JsonArray(Scripts.Types.Select(t => (JsonNode)t.Name).ToArray()),
        };
        return tree.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    // ---- attach_component ------------------------------------------------------------------------

    private string AttachComponent(JsonElement args)
    {
        string type = Arg(args, "type");
        string path = Arg(args, "path");
        string where = args.TryGetProperty("where", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString()! : "self";
        if (Playing) throw new InvalidOperationException("Stop playing before changing the scene.");

        var target = FindObject(path) ?? throw new ArgumentException($"No object '{path}' (see get_tree).");
        var targets = where switch
        {
            "self" => new List<GameObject> { target },
            "children" => new[] { target }.Concat(Descendants(target)).ToList(),
            _ => throw new ArgumentException("where must be 'self' or 'children'."),
        };

        if (type.Equals("PointLight", StringComparison.OrdinalIgnoreCase) || type.Equals("SpotLight", StringComparison.OrdinalIgnoreCase))
        {
            bool point = type.StartsWith("Point", StringComparison.OrdinalIgnoreCase);
            foreach (var t in targets)
            {
                if (point)
                {
                    var light = new PointLight(t.WorldPosition, Color4.White, 3f, 12f) { Name = UniqueLightName($"{t.Name} Light") };
                    Scene.AddLight(light);
                    if (!Scene.PointLights.Contains(light)) throw new InvalidOperationException($"A scene can have {Scene.MaxPointLights} point lights.");
                    light.AttachTo(t);
                }
                else
                {
                    if (Scene.SpotLights.Count >= Scene.MaxSpotLights) throw new InvalidOperationException($"A scene can have {Scene.MaxSpotLights} spot lights.");
                    var light = new SpotLight(t.WorldPosition + Vector3.UnitY, -Vector3.UnitY) { Name = UniqueLightName($"{t.Name} Light"), Intensity = 4f, Range = 25f };
                    Scene.AddLight(light);
                    light.AttachTo(t);
                }
            }
            return $"Attached a {type} to {targets.Count} object(s).";
        }

        var scriptType = Scripts.Types.FirstOrDefault(s => s.Name.Equals(type, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown component '{type}'. Script types: {string.Join(", ", Scripts.Types.Select(s => s.Name))}; or PointLight / SpotLight.");
        foreach (var t in targets) t.AddScript(scriptType);
        return $"Attached script {scriptType.Name} to {targets.Count} object(s).";
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static string Arg(JsonElement args, string key) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s
            ? s
            : throw new ArgumentException($"Missing argument '{key}'.");

    private static string Fmt(Vector3 v) => $"({v.X:0.##}, {v.Y:0.##}, {v.Z:0.##})";

    private static IEnumerable<GameObject> Descendants(GameObject o) => o.Children.Where(c => !c.EditorOnly).SelectMany(c => new[] { c }.Concat(Descendants(c)));

    /// <summary>An object by name (anywhere in the scene) or by path ("Parent/Child").</summary>
    private GameObject? FindObject(string path)
    {
        if (!path.Contains('/')) return Scene.Traverse().FirstOrDefault(o => !o.EditorOnly && o.Name == path);

        GameObject? current = null;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidates = current == null ? Scene.Roots : current.Children;
            current = candidates.FirstOrDefault(o => !o.EditorOnly && o.Name == part);
            if (current == null) return null;
        }
        return current;
    }

    /// <summary>Sets public properties/fields by name (case-insensitive) from a JSON object.</summary>
    private static void ApplyProperties(object target, JsonElement properties)
    {
        if (properties.ValueKind != JsonValueKind.Object) return;
        foreach (var entry in properties.EnumerateObject())
        {
            var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
            string key = target is GameObject && entry.Name.Equals("rotation", StringComparison.OrdinalIgnoreCase) ? "EulerAngles" : entry.Name;   // degrees
            var property = target.GetType().GetProperty(key, flags);
            var field = property == null ? target.GetType().GetField(key, flags) : null;
            var type = property?.PropertyType ?? field?.FieldType;
            if (type == null || (property != null && !property.CanWrite))
                throw new ArgumentException($"'{entry.Name}' is not a settable property of {target.GetType().Name}.");

            object value = Convert(entry.Value, type, entry.Name);
            if (property != null) property.SetValue(target, value); else field!.SetValue(target, value);
        }
    }

    private static object Convert(JsonElement v, Type type, string key)
    {
        try
        {
            if (type == typeof(bool)) return v.GetBoolean();
            if (type == typeof(int)) return v.GetInt32();
            if (type == typeof(float)) return v.GetSingle();
            if (type == typeof(string)) return v.GetString() ?? "";
            if (type.IsEnum) return Enum.Parse(type, v.GetString()!, ignoreCase: true);
            if (type == typeof(Vector3))
            {
                if (v.ValueKind == JsonValueKind.Number) return new Vector3(v.GetSingle());
                var a = v.EnumerateArray().Select(e => e.GetSingle()).ToArray();
                if (a.Length == 3) return new Vector3(a[0], a[1], a[2]);
            }
            else if (type == typeof(Color4))
            {
                if (v.ValueKind == JsonValueKind.String)
                {
                    string hex = v.GetString()!.TrimStart('#');
                    if (hex.Length == 6) return new Color4(System.Convert.ToByte(hex[..2], 16), System.Convert.ToByte(hex[2..4], 16), System.Convert.ToByte(hex[4..], 16), 255);
                }
                else
                {
                    var a = v.EnumerateArray().Select(e => e.GetSingle()).ToArray();
                    if (a.Length is 3 or 4) return new Color4(a[0], a[1], a[2], a.Length == 4 ? a[3] : 1f);
                }
            }
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or ArgumentException)
        {
            // fall through to the message below
        }
        throw new ArgumentException($"Bad value for '{key}': expected {type.Name} (vectors as [x,y,z], colours as [r,g,b,a] or \"#rrggbb\").");
    }
}
