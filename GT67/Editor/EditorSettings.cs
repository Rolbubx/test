using System.Text.Json;
using System.Text.Json.Serialization;

namespace GT67.Editor;

/// <summary>Small preferences of the editor, kept in <c>editor.settings.json</c> next to the project file.</summary>
public sealed class EditorSettings
{
    private const string FileName = "editor.settings.json";

    /// <summary>The program that opens scripts: "code" (Visual Studio Code), "rider", "notepad"... or "default".</summary>
    public string ExternalEditor { get; set; } = "code";

    /// <summary>Recompile the scripts automatically when a file in Scripts/ is saved.</summary>
    public bool AutoCompile { get; set; } = true;

    public bool SnapEnabled { get; set; }
    public float MoveSnap { get; set; } = 0.5f;
    public float RotateSnap { get; set; } = 15f;
    public float ScaleSnap { get; set; } = 0.1f;

    /// <summary>Temporary switch for testing, mirrored into <see cref="Utils.DebugFlag.On"/>.</summary>
    [JsonPropertyName("debug_flag")]
    public bool DebugFlag
    {
        get => Utils.DebugFlag.On;
        set => Utils.DebugFlag.On = value;
    }

    public static EditorSettings Load(ProjectPaths? paths)
    {
        if (paths == null) return new EditorSettings();
        try
        {
            string file = Path.Combine(paths.Root, FileName);
            if (File.Exists(file)) return JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(file)) ?? new EditorSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // fall back to the defaults
        }
        return new EditorSettings();
    }

    public void Save(ProjectPaths? paths)
    {
        if (paths == null) return;
        try
        {
            File.WriteAllText(Path.Combine(paths.Root, FileName), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Utils.Log.Warn($"Could not save the editor settings: {e.Message}");
        }
    }
}
