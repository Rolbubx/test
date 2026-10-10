using GT67.Core;
using static GT67.Editor.Export.CodeWriter;

namespace GT67.Editor.Export;

/// <summary>Writes TerrainGenerator.cs: the sculpted heights and painted colours of every terrain.</summary>
public static class TerrainExporter
{
    public const string FileName = "TerrainGenerator.cs";

    public static string Write(ExportContext context)
    {
        var w = new CodeWriter("The terrains of the game: size, sculpted heights and painted colours.");

        w.Line("/// <summary>Terrains with their heights and paint.</summary>");
        w.Open("public static class TerrainGenerator");
        w.Open("public static void Build(Scene scene)");
        foreach (var terrain in context.Scene.Terrains)
        {
            context.Terrains++;
            w.Line("{");
            w.Indent++;
            w.Line($"var terrain = new Terrain({F(terrain.Width)}, {F(terrain.Depth)}, {terrain.Resolution}, 0f);");
            w.Line($"terrain.Object.Name = {Str(terrain.Object.Name)};");
            w.Line($"terrain.Object.Position = {V3(terrain.Object.Position)};");
            w.Line("terrain.Import(");
            w.Indent++;
            w.Line(LongString(terrain.ExportHeights()) + ",");
            w.Line(LongString(terrain.ExportColors()) + ");");
            w.Indent--;
            w.Line("scene.Add(terrain);");
            w.Indent--;
            w.Line("}");
        }
        w.Close();
        w.Close();
        return w.ToString();
    }
}
