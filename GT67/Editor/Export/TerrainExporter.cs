using GT67.Core;
using static GT67.Editor.Export.CodeWriter;

namespace GT67.Editor.Export;

/// <summary>
/// Writes TerrainGenerator.cs. The sculpted heights and painted colours of every terrain go to a binary file in
/// <c>Assets/Terrains</c> (see <see cref="Terrain.SaveData"/>), which the generated code loads at startup.
/// </summary>
public static class TerrainExporter
{
    public const string FileName = "TerrainGenerator.cs";

    public static string Write(ExportContext context)
    {
        var w = new CodeWriter("The terrains of the game: size, sculpted heights and painted colours.", context.Namespace);

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
            string name = Identifier(terrain.Object.Name) + ".terrain";
            if (context.Assets.WriteData("Terrains", name, terrain.SaveData) is { } data)
                w.Line($"terrain.LoadData({Str(data)});");
            w.Line("scene.Add(terrain);");
            w.Indent--;
            w.Line("}");
        }
        w.Close();
        w.Close();
        return w.ToString();
    }
}
