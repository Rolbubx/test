using GT67.Core;
using static GT67.Editor.Export.CodeWriter;

namespace GT67.Editor.Export;

/// <summary>Writes WaterGenerator.cs: lakes and seas with all their settings.</summary>
public static class WaterExporter
{
    public const string FileName = "WaterGenerator.cs";

    public static string Write(ExportContext context)
    {
        var w = new CodeWriter("The bodies of water of the game.", context.Namespace);

        w.Line("/// <summary>Lakes, ponds and seas.</summary>");
        w.Open("public static class WaterGenerator");
        w.Open("public static void Build(Scene scene)");
        foreach (var water in context.Scene.Waters)
        {
            context.Waters++;
            w.Line($"scene.Add(new Water({F(water.Position.X)}, {F(water.Position.Y)}, {F(water.Position.Z)}, " +
                   $"{F(water.Width)}, {F(water.Depth)})");
            w.Initializer(water, null, exclude: new[] { "Position", "Level", "Width", "Depth" }, extra: new[] { $"Name = {Str(water.Name)}" }, closing: ");");
        }
        w.Close();
        w.Close();
        return w.ToString();
    }
}
