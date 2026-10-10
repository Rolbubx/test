using GT67.Core;
using GT67.UI;

namespace GT67.Editor.Export;

/// <summary>What every exporter needs: the editor's scene, the places to write to and counters for the final report.</summary>
public sealed class ExportContext
{
    private int _next;

    public ExportContext(Scene scene, ProjectPaths paths, UiCanvas ui, PostProcess post, AssetShipper assets, List<string> warnings)
    {
        Scene = scene;
        Paths = paths;
        Ui = ui;
        Post = post;
        Assets = assets;
        Warnings = warnings;
    }

    public Scene Scene { get; }
    public ProjectPaths Paths { get; }
    public UiCanvas Ui { get; }
    public PostProcess Post { get; }
    public AssetShipper Assets { get; }
    public List<string> Warnings { get; }

    public int Objects { get; set; }
    public int Terrains { get; set; }
    public int Waters { get; set; }
    public int UiElements { get; set; }
    public int Sounds { get; set; }

    /// <summary>A number that makes variable names unique within a file.</summary>
    public int NextId() => _next++;
}
