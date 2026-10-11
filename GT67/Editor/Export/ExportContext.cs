using GT67.Core;
using GT67.UI;

namespace GT67.Editor.Export;

/// <summary>What every exporter needs: the scene being written, the places to write to and counters for the final report.</summary>
public sealed class ExportContext
{
    /// <summary>The namespace of the game-wide files (GameGenerator, UiGenerator); each scene's files get one below it.</summary>
    public const string RootNamespace = "GT67.Generated";

    private int _next;

    public ExportContext(Scene scene, ProjectPaths paths, UiCanvas ui, AssetShipper assets, List<string> warnings,
        string ns = RootNamespace)
    {
        Scene = scene;
        Paths = paths;
        Ui = ui;
        Assets = assets;
        Warnings = warnings;
        Namespace = ns;
    }

    public Scene Scene { get; }
    public ProjectPaths Paths { get; }
    public UiCanvas Ui { get; }
    public AssetShipper Assets { get; }
    public List<string> Warnings { get; }

    /// <summary>The namespace the files of this scene are written in.</summary>
    public string Namespace { get; }

    public int Objects { get; set; }
    public int Terrains { get; set; }
    public int Waters { get; set; }
    public int UiElements { get; set; }
    public int Sounds { get; set; }

    /// <summary>A number that makes variable names unique within a file.</summary>
    public int NextId() => _next++;
}
