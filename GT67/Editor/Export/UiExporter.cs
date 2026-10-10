using GT67.UI;
using static GT67.Editor.Export.CodeWriter;

namespace GT67.Editor.Export;

/// <summary>Writes UiGenerator.cs: the game UI designed in the editor (HUD, menus), with its images shipped beside the code.</summary>
public static class UiExporter
{
    public const string FileName = "UiGenerator.cs";

    public static string Write(ExportContext context)
    {
        var ui = context.Ui;
        var w = new CodeWriter("The game UI designed in the editor; its images are shipped in Assets/UI.");

        w.Line("/// <summary>The HUD and menus of the game.</summary>");
        w.Open("public static class UiGenerator");
        w.Open("public static void Build(UiCanvas ui)");
        if (Math.Abs(ui.Scale - 1f) > 1e-4f) w.Line($"ui.Scale = {F(ui.Scale)};");
        foreach (var root in ui.Roots)
        {
            string variable = WriteElement(context, w, root);
            w.Line($"ui.Add({variable});");
        }
        w.Close();
        w.Close();
        return w.ToString();
    }

    private static string WriteElement(ExportContext context, CodeWriter w, UiElement element)
    {
        string v = $"u{context.NextId()}";
        context.UiElements++;

        var defaults = new UiElement(element.Kind);
        var extra = new List<string> { $"Name = {Str(element.Name)}" };
        if (element.Text != defaults.Text) extra.Add($"Text = {Str(element.Text)}");
        if (element.Hint != defaults.Hint) extra.Add($"Hint = {Str(element.Hint)}");
        if (element.Action.Length > 0) extra.Add($"Action = {Str(element.Action)}");
        if (element.ImagePath.Length > 0) extra.Add($"ImagePath = {Str(ShipImage(context, element))}");

        w.Line($"var {v} = new UiElement(global::GT67.UI.UiKind.{element.Kind})");
        w.Initializer(element, defaults, extra: extra.ToArray());

        foreach (var child in element.Children)
        {
            string childVariable = WriteElement(context, w, child);
            w.Line($"{v}.Add({childVariable});");
        }
        return v;
    }

    private static string ShipImage(ExportContext context, UiElement element)
    {
        var shipped = context.Assets.ShipFile(element.ImagePath, "UI");
        if (shipped != null) return shipped;
        context.Warnings.Add($"UI element '{element.Name}': image '{element.ImagePath}' was not found.");
        return element.ImagePath;
    }
}
