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
        w.Close();
        w.Close();
        return w.ToString();
    }
}
