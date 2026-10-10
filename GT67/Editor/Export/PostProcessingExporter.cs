using static GT67.Editor.Export.CodeWriter;
using GT67.Core;

namespace GT67.Editor.Export;

/// <summary>
/// Writes PostProcessingGenerator.cs: the tone mapping and every effect setting as Post.Set calls. All values are
/// written (not only changes), so what the editor shows wins over the start-up code in Program.cs.
/// </summary>
public static class PostProcessingExporter
{
    public const string FileName = "PostProcessingGenerator.cs";

    public static string Write(ExportContext context)
    {
        var post = context.Post;
        var w = new CodeWriter("The Main Volume: tone mapping and post-processing effect settings.");

        w.Line("/// <summary>Bloom, depth of field, colour grading and the other screen effects.</summary>");
        w.Open("public static class PostProcessingGenerator");
        w.Open("public static void Build(PostProcess post)");
        foreach (var effect in post.Effects)
            foreach (var parameter in ParameterAccess.Describe(effect))
                w.Line($"post.Set({Str(effect.Name)}, {Str(parameter.Name)}, {Literal(parameter.Value!)});");
        w.Line($"post.Enabled = {Bool(post.Enabled)};");
        w.Line($"post.Exposure = {F(post.Exposure)};");
        w.Line($"post.ToneMap = {Literal(post.ToneMap)};");
        w.Close();
        w.Close();
        return w.ToString();
    }
}
