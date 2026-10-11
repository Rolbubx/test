using GT67.Core;
using static GT67.Editor.Export.CodeWriter;

namespace GT67.Editor.Export;

/// <summary>Writes EnvironmentGenerator.cs: sun, rain, fog, ambient light, sky with clouds, point lights and the spot light.</summary>
public static class EnvironmentExporter
{
    public const string FileName = "EnvironmentGenerator.cs";

    public static string Write(ExportContext context)
    {
        var scene = context.Scene;
        var w = new CodeWriter("The weather, sky and lights of the game.", context.Namespace);

        w.Line("/// <summary>Sun, rain, fog, ambient light, sky, clouds and lights.</summary>");
        w.Open("public static class EnvironmentGenerator");
        w.Open("public static void Build(Scene scene)");

        if (scene.Sun is { } sun)
        {
            w.Line("scene.Sun = new Sun");
            w.Initializer(sun, new Sun(), exclude: new[] { "Direction", "AutoPosition" });
            if (!sun.AutoPosition) w.Line($"scene.Sun.Direction = {V3(sun.Direction)};");
        }

        if (scene.Rain is { } rain)
        {
            w.Line("scene.Rain = new Rain");
            w.Initializer(rain, new Rain());
        }
        if (scene.Fog != 0f) w.Line($"scene.Fog = {F(scene.Fog)};");
        if (scene.PuddleLevel != 0f) w.Line($"scene.PuddleLevel = {F(scene.PuddleLevel)};");

        w.Line($"scene.AmbientColor = {Col(scene.AmbientColor)};");
        w.Line($"scene.AmbientIntensity = {F(scene.AmbientIntensity)};");

        if (scene.Skybox is { } skybox)
        {
            if (skybox.Physical is { } sky)
            {
                w.Line("{");
                w.Indent++;
                w.Line("var sky = new PhysicalSky");
                w.Initializer(sky, new PhysicalSky());
                if (sky.Clouds is { } clouds)
                {
                    w.Line("sky.Clouds = new CloudLayer");
                    w.Initializer(clouds, new CloudLayer());
                }
                w.Line("scene.Skybox = new Skybox(sky);");
                w.Indent--;
                w.Line("}");
            }
            else if (skybox.Cubemap?.SourceDirectory is { } directory && context.Paths.ToProjectRelative(directory) is { } relative)
            {
                w.Line($"scene.Skybox = new Skybox(Cubemap.TryFromFolder(ModelCache.ResolvePath({Str(relative)})));");
            }
            else
            {
                w.Line("scene.Skybox = new Skybox");
                w.Initializer(skybox, new Skybox(), exclude: new[] { "CustomShader" });
            }

            // The gradient, drawn when the physical sky is switched off
            if (skybox.Physical != null || skybox.Cubemap != null)
            {
                using var defaults = new Skybox();
                if (skybox.TopColor != defaults.TopColor) w.Line($"scene.Skybox.TopColor = {Col(skybox.TopColor)};");
                if (skybox.HorizonColor != defaults.HorizonColor) w.Line($"scene.Skybox.HorizonColor = {Col(skybox.HorizonColor)};");
                if (skybox.BottomColor != defaults.BottomColor) w.Line($"scene.Skybox.BottomColor = {Col(skybox.BottomColor)};");
            }

            // A sky shader (a .shader file with a 'sky' block), shipped with its settings
            if (skybox.CustomShader is { } use)
            {
                if (!ObjectsExporter.ShipShader(context, use.Name))
                    context.Warnings.Add($"Scene '{scene.Name}': sky shader '{use.Name}.shader' not found, so the game draws the sky without it.");
                w.Line("{");
                w.Indent++;
                w.Line($"var skyShader = scene.Skybox.CustomShader = new ScreenShaderUse({Str(use.Name)});");
                PostProcessingExporter.WriteShaderSettings(context, w, "skyShader", use);
                w.Indent--;
                w.Line("}");
            }
        }

        foreach (var light in scene.PointLights)
        {
            w.Line($"scene.AddLight(new PointLight({V3(light.Position)}, {Col(light.Color)}, {F(light.Intensity)}, {F(light.Range)}) " +
                   $"{{ Name = {Str(light.Name)}, Enabled = {Bool(light.Enabled)} }});");
        }
        if (scene.Spot is { } spot)
        {
            w.Line($"scene.Spot = new SpotLight({V3(spot.Position)}, {V3(spot.Direction)})");
            w.Initializer(spot, null, exclude: new[] { "Position", "Direction" });
        }

        w.Close();
        w.Close();
        return w.ToString();
    }
}
