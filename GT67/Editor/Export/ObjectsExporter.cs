using GT67.Core;
using OpenTK.Mathematics;
using static GT67.Editor.Export.CodeWriter;

namespace GT67.Editor.Export;

/// <summary>
/// Writes ObjectsGenerator.cs: every object of the scene (models, primitives, transforms, materials, attached
/// scripts and children), one method per root object. Model files (.fbx, .gltf, .obj, ...) and their textures are
/// shipped beside the code and loaded from there.
/// </summary>
public static class ObjectsExporter
{
    public const string FileName = "ObjectsGenerator.cs";

    public static string Write(ExportContext context)
    {
        var scene = context.Scene;
        var w = new CodeWriter("The objects of the game; imported model files are shipped in Assets/Models.", context.Namespace);
        var terrainObjects = scene.Terrains.Select(t => t.Object).ToHashSet();

        var roots = scene.Roots.Where(r => !r.EditorOnly && !terrainObjects.Contains(r)).ToList();
        var methods = new List<string>();

        // Method bodies first, then the class around them.
        var body = new CodeWriter { Indent = 1 };
        for (int i = 0; i < roots.Count; i++)
        {
            string method = $"Create{i + 1:000}_{Identifier(roots[i].Name)}";
            methods.Add(method);
            body.Line($"/// <summary>{System.Security.SecurityElement.Escape(roots[i].Name)}</summary>");
            body.Open($"private static void {method}(Scene scene)");
            string variable = WriteObject(context, body, roots[i]);
            body.Line($"scene.Add({variable});");
            body.Close();
            body.Line();
        }

        w.Line("/// <summary>Everything placed in the scene.</summary>");
        w.Open("public static class ObjectsGenerator");
        w.Open("public static void Build(Scene scene)");
        foreach (var method in methods) w.Line($"{method}(scene);");
        w.Close();
        w.Line();
        w.Raw(body.ToString());
        w.Close();
        return w.ToString();
    }

    /// <summary>Emits the statements that create <paramref name="obj"/> and its children; returns the variable name.</summary>
    private static string WriteObject(ExportContext context, CodeWriter w, GameObject obj)
    {
        string v = $"o{context.NextId()}";
        context.Objects++;
        var p = obj.Position;

        w.Line($"var {v} = new GameObject({MeshExpression(context, obj)}, {F(p.X)}, {F(p.Y)}, {F(p.Z)})");
        var flags = new List<string> { $"Name = {Str(obj.Name)}" };
        if (obj.Tag.Length > 0) flags.Add($"Tag = {Str(obj.Tag)}");
        if (obj.PrefabName != null) flags.Add($"PrefabName = {Str(obj.PrefabName)}");
        if (!obj.Enabled) flags.Add("Enabled = false");
        if (!obj.Visible) flags.Add("Visible = false");
        if (!obj.CastShadow) flags.Add("CastShadow = false");
        if (!obj.ReceiveShadow) flags.Add("ReceiveShadow = false");
        if (obj.Wireframe) flags.Add("Wireframe = true");
        if (!obj.CullFace) flags.Add("CullFace = false");
        if (obj.Collision != CollisionMode.Mesh) flags.Add($"Collision = {Literal(obj.Collision)}");
        w.Line("{ " + string.Join(", ", flags) + " };");

        if (obj.EulerAngles != Vector3.Zero) w.Line($"{v}.EulerAngles = {V3(obj.EulerAngles)};");
        if (obj.Scale != Vector3.One) w.Line($"{v}.Scale = {V3(obj.Scale)};");

        var defaults = new Material();
        foreach (var parameter in ParameterAccess.Describe(obj.Material))
            if (!Equals(parameter.Value, ParameterAccess.Get(defaults, parameter.Name)))
                w.Line($"{v}.Material.{parameter.Name} = {Literal(parameter.Value!)};");

        if (obj.Material.DiffuseMap is { } texture)
        {
            string? path = context.Assets.ShipTexture(texture);
            if (path != null) w.Line($"{v}.SetTexture({Str(path)});");
            else if (texture.Origin != null) w.Line($"{v}.SetTexture({texture.Origin});");
            else context.Warnings.Add($"'{obj.Name}': the texture is not a file, so it was left out.");
        }

        WriteCustomShader(context, w, v, obj);

        foreach (var script in obj.Scripts)
        {
            var type = script.GetType();
            w.Line($"{v}.AddScript(new global::{type.FullName!.Replace('+', '.')}");
            object? scriptDefaults = null;
            try { scriptDefaults = Activator.CreateInstance(type); } catch (MissingMethodException) { }
            w.Initializer(script, scriptDefaults, closing: ");");
        }

        foreach (var child in obj.Children)
        {
            if (child.EditorOnly) continue;
            string childVariable = WriteObject(context, w, child);
            w.Line($"{v}.AddChild({childVariable});");
        }
        return v;
    }

    /// <summary>
    /// The object's custom shader (a .shader file): its name, the values and images the object gives its parameters,
    /// and the file itself shipped into Assets/Shaders, where the game's shader library finds it.
    /// </summary>
    private static void WriteCustomShader(ExportContext context, CodeWriter w, string v, GameObject obj)
    {
        var material = obj.Material;
        if (material.CustomShader.Length == 0) return;

        if (!ShipShader(context, material.CustomShader))
            context.Warnings.Add($"'{obj.Name}': shader file '{material.CustomShader}{ShaderLibrary.Extension}' not found, so the game draws it normally.");

        w.Line($"{v}.SetShader({Str(material.CustomShader)});");
        foreach (var (name, value) in material.ShaderParams)
            w.Line($"{v}.SetShaderParam({Str(name)}, {F(value.X)}, {F(value.Y)}, {F(value.Z)});");
        foreach (var (name, path) in material.ShaderTextures)
        {
            if (path.Length == 0) continue;
            string? shipped = context.Assets.ShipFile(path, "Textures");
            if (shipped != null) w.Line($"{v}.SetShaderTexture({Str(name)}, {Str(shipped)});");
            else context.Warnings.Add($"'{obj.Name}': shader texture '{path}' not found, so it was left out.");
        }
    }

    /// <summary>Ships a .shader file into Assets/Shaders, where the game's shader library finds it. False if it is not found.</summary>
    internal static bool ShipShader(ExportContext context, string name)
    {
        string? file = new[]
            {
                Path.Combine(context.Paths.Root, "Shaders", "Custom"),
                Path.Combine(AppContext.BaseDirectory, "Shaders", "Custom"),
                Path.Combine(AppContext.BaseDirectory, "Assets", "Game", "Shaders"),
            }
            .Select(folder => Path.Combine(folder, name + ShaderLibrary.Extension))
            .FirstOrDefault(File.Exists);
        return file != null && context.Assets.ShipFile(file, "Shaders") != null;
    }

    private static string MeshExpression(ExportContext context, GameObject obj)
    {
        var model = obj.Model;
        if (model.SourcePath != null)
        {
            string? path = context.Assets.ShipModel(model);
            if (path != null) return $"GT67.Utils.Assets.LoadModel({Str(path)})";
            context.Warnings.Add($"'{obj.Name}': model file '{model.SourcePath}' could not be shipped; the object was baked from its mesh.");
        }

        if (model.Parts.Count == 1 && model.Parts[0].Mesh.Origin is { } origin) return origin;

        // No known recipe: ship the vertices themselves in Assets/Meshes.
        if (model.Parts.Count > 1)
            context.Warnings.Add($"'{obj.Name}' has {model.Parts.Count} parts and no model file; only the first part was saved.");
        var mesh = model.Parts[0].Mesh;
        if (context.Assets.WriteData("Meshes", Identifier(obj.Name) + ".mesh", mesh.SaveData) is { } data)
            return $"Mesh.LoadData({Str(data)})";
        context.Warnings.Add($"'{obj.Name}': its mesh could not be saved, so it became a cube.");
        return "Primitives.Cube(1f)";
    }
}
