using static GT67.Editor.Export.CodeWriter;

namespace GT67.Editor.Export;

/// <summary>
/// Writes a scene's PostProcessingGenerator.cs: its Volume (tone mapping and every effect setting) as Volume.Set calls,
/// applied when the scene becomes active. All values are written (not only changes), so what the editor shows wins
/// over the start-up code in Program.cs.
/// </summary>
public static class PostProcessingExporter
{
    public const string FileName = "PostProcessingGenerator.cs";

    public static string Write(ExportContext context)
    {
        var w = new CodeWriter("The scene's Volume: tone mapping and post-processing effect settings.", context.Namespace);

        w.Line("/// <summary>Bloom, depth of field, colour grading and the other screen effects of this scene.</summary>");
        w.Open("public static class PostProcessingGenerator");
        w.Open("public static void Build(Scene scene)");
        foreach (var (effect, parameter, value) in context.Scene.Volume.Values)
            if (value != null)
                w.Line($"scene.Volume.Set({Str(effect)}, {Str(parameter)}, {Literal(value)});");

        // Screen shaders (PS1, CRT, your own): the .shader files ship into Assets/Shaders with their images.
        foreach (var screen in context.Scene.Volume.ScreenShaders)
        {
            if (!ObjectsExporter.ShipShader(context, screen.Name))
                context.Warnings.Add($"Scene '{context.Scene.Name}': screen shader '{screen.Name}.shader' not found, so the game runs without it.");
            w.Line("{");
            w.Indent++;
            w.Line($"var screen = scene.Volume.AddScreenShader({Str(screen.Name)});");
            if (!screen.Enabled) w.Line("screen.Enabled = false;");
            foreach (var (name, v) in screen.Values)
                w.Line($"screen.Set({Str(name)}, new Vector4({F(v.X)}, {F(v.Y)}, {F(v.Z)}, {F(v.W)}));");
            foreach (var (name, path) in screen.Textures)
            {
                if (path.Length == 0) continue;
                if (context.Assets.ShipFile(path, "Textures") is { } shipped) w.Line($"screen.SetTexture({Str(name)}, {Str(shipped)});");
                else context.Warnings.Add($"Screen shader '{screen.Name}': image '{path}' not found, so it was left out.");
            }
            w.Indent--;
            w.Line("}");
        }
        w.Close();
        w.Close();
        return w.ToString();
    }
}
