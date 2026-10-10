using GT67.Core;
using static GT67.Editor.Export.CodeWriter;

namespace GT67.Editor.Export;

/// <summary>Writes GrassGenerator.cs: the grass settings and every blade (as base64).</summary>
public static class GrassExporter
{
    public const string FileName = "GrassGenerator.cs";

    public static string Write(ExportContext context)
    {
        var w = new CodeWriter("The grass of the game: its look and every painted blade.");

        w.Line("/// <summary>The grass layer with its blades.</summary>");
        w.Open("public static class GrassGenerator");
        w.Open("public static void Build(Scene scene)");
        if (context.Scene.Grass is { } grass && grass.Count > 0)
        {
            w.Line("scene.Grass = new GrassLayer");
            w.Initializer(grass, new GrassLayer(), closing: ";");
            w.Line("scene.Grass.Import(");
            w.Indent++;
            w.Line(LongString(grass.Export()) + ");");
            w.Indent--;
        }
        w.Close();
        w.Close();
        return w.ToString();
    }
}
