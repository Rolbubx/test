using GT67.Core;
using static GT67.Editor.Export.CodeWriter;

namespace GT67.Editor.Export;

/// <summary>
/// Writes SoundsGenerator.cs: every <see cref="Sound"/> of the scene, with its sound file shipped beside the code, and
/// what the sounds and lights are attached to (it runs after the objects are built).
/// </summary>
public static class SoundsExporter
{
    public const string FileName = "SoundsGenerator.cs";

    public static string Write(ExportContext context)
    {
        var w = new CodeWriter("The sounds of the game; the sound files are shipped in Assets/Sounds.", context.Namespace);

        w.Line("/// <summary>Sounds with their volume, pitch, looping and 3D settings.</summary>");
        w.Open("public static class SoundsGenerator");
        w.Open("public static void Build(Scene scene)");
        foreach (var sound in context.Scene.Sounds)
        {
            context.Sounds++;
            string file = sound.File;
            if (file.Length > 0)
            {
                var shipped = context.Assets.ShipFile(file, "Sounds");
                if (shipped != null) file = shipped;
                else context.Warnings.Add($"Sound '{sound.Name}': the file '{sound.File}' was not found, so it cannot be shipped.");
            }

            string v = $"sound{context.NextId()}";
            w.Line($"var {v} = new Sound({Str(file)})");
            w.Initializer(sound, new Sound(), extra: new[] { $"Name = {Str(sound.Name)}" });
            if (sound.Follow is { IsDestroyed: false } target)
                w.Line($"{v}.Follow = scene.Find({Str(target.Name)});");   // objects are built before sounds
            w.Line($"scene.Add({v});");
        }

        // Lights attached to objects (the lights are made with the environment, before the objects exist)
        var scene = context.Scene;
        for (int i = 0; i < scene.PointLights.Count; i++)
            if (scene.PointLights[i].Follow is { IsDestroyed: false } target)
            {
                w.Line($"scene.PointLights[{i}].Follow = scene.Find({Str(target.Name)});");
                w.Line($"scene.PointLights[{i}].FollowOffset = {V3(scene.PointLights[i].FollowOffset)};");
            }
        for (int i = 0; i < scene.SpotLights.Count; i++)
            if (scene.SpotLights[i].Follow is { IsDestroyed: false } target)
                w.Line($"scene.SpotLights[{i}].Follow = scene.Find({Str(target.Name)});");   // offset and direction are in its initializer
        w.Close();
        w.Close();
        return w.ToString();
    }
}
