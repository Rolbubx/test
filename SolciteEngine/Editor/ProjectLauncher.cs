using SolciteEngine.Core;
using SolciteEngine.Gui;

namespace SolciteEngine.Editor;

/// <summary>
/// The first screen of the editor: pick a recent project, open a project folder (typed or dropped onto the window) or
/// create a new one. Once <see cref="Chosen"/> is set the window starts the editor on that project.
/// </summary>
public sealed class ProjectLauncher
{
    private const float CardWidth = 760f, CardHeight = 540f;

    private readonly Window _window;
    private readonly ProjectPaths? _sandbox = ProjectPaths.Locate();
    private List<RecentProjects.Entry> _recent = RecentProjects.Load();

    private int _tab;
    private string? _selected;       // a recent project's folder, or "" for the sandbox
    private string _openPath = "";
    private string _newName = "MyGame";
    private string _newLocation = ProjectPaths.DefaultLocation();
    private string _error = "";

    public ProjectLauncher(Window window)
    {
        _window = window;
        if (_recent.Count == 0) _tab = 1;   // nothing to open yet: start on "New project"
        else _selected = _recent[0].Path;
    }

    /// <summary>The project to open; set when the user picked or created one.</summary>
    public ProjectPaths? Chosen { get; private set; }

    public void Draw(float deltaTime)
    {
        var g = _window.Ui.Gui;
        var size = _window.Viewport;
        const WindowFlags background = WindowFlags.Fixed | WindowFlags.Panel | WindowFlags.NoScroll;
        if (g.Begin("LauncherBackground", new UiRect(0f, 0f, size.Width, size.Height), background)) { }
        g.End();

        float width = MathF.Min(CardWidth, size.Width - 32f), height = MathF.Min(CardHeight, size.Height - 32f);
        var card = new UiRect((size.Width - width) * 0.5f, (size.Height - height) * 0.5f, width, height);
        if (g.Begin("LauncherCard", card, WindowFlags.Fixed | WindowFlags.NoScroll))
        {
            g.Spacing(6f);
            g.Heading("SolciteEngine");
            g.TextDim("Open a project or create a new one.");
            g.Spacing(8f);

            _tab = g.TabBar("launchertabs", new[] { "Projects", "New project" }, _tab, new[] { IconKind.Folder, IconKind.Plus });
            g.Spacing(8f);

            if (_tab == 0) DrawOpenTab(g, height);
            else DrawNewTab(g);

            if (_error.Length > 0)
            {
                g.Spacing(6f);
                g.TextStatus(_error, error: true);
            }
        }
        g.End();
    }

    private void DrawOpenTab(Gui.Gui g, float cardHeight)
    {
        if (g.BeginChild("recentprojects", MathF.Max(120f, cardHeight - 300f)))
        {
            if (_recent.Count == 0 && _sandbox == null)
                g.TextWrapped("No projects yet. Create one in the \"New project\" tab, or open an existing project folder below.", dim: true);

            foreach (var entry in _recent)
            {
                if (g.Selectable($"{entry.Name}    {entry.Path}##recent{entry.Path}", _selected == entry.Path, IconKind.Folder, 30f))
                {
                    _selected = entry.Path;
                    _error = "";
                }
                if (g.LastItemDoubleClicked) OpenFolder(entry.Path);
            }

            if (_sandbox != null)
            {
                g.Spacing(4f);
                g.SeparatorText("Engine");
                if (g.Selectable($"SolciteEngine sandbox    {_sandbox.Root}##sandbox", _selected == "", IconKind.Cube, 30f))
                {
                    _selected = "";
                    _error = "";
                }
                if (g.LastItemDoubleClicked) Choose(_sandbox);
            }
        }
        g.EndChild();

        g.Spacing(6f);
        bool canOpen = _selected != null;
        if (g.Button("Open selected project", 220f, canOpen ? ButtonStyle.Primary : ButtonStyle.Normal) && canOpen)
        {
            if (_selected == "" && _sandbox != null) Choose(_sandbox);
            else if (_selected != null) OpenFolder(_selected);
        }

        g.Spacing(8f);
        g.SeparatorText("Open a project folder");
        g.InputText("Folder", ref _openPath, "path to a Project-<name> folder (it contains infos.json)");
        if (g.Button("Open folder", 160f)) OpenFolder(_openPath);
        g.SameLine();
        g.TextDim("or drop the folder onto this window");
    }

    private void DrawNewTab(Gui.Gui g)
    {
        g.InputText("Name", ref _newName, "MyGame");
        g.InputText("Location", ref _newLocation, "folder that will contain the project");
        g.Spacing(4f);

        string clean = ProjectPaths.CleanName(_newName);
        g.TextDim(clean.Length == 0
            ? "The project folder will be created inside the location."
            : "Creates: " + Path.Combine(_newLocation.Trim(), ProjectPaths.FolderPrefix + clean));
        g.Spacing(8f);
        g.TextWrapped("A project is a Project-<name> folder with infos.json, assets (scripts, models, textures, sounds, prefabs, shaders), " +
                      "cache and the game the editor writes when " +
                      "you press Compile (generated-game).", dim: true);
        g.Spacing(10f);

        if (g.Button("Create project", 200f, ButtonStyle.Primary))
        {
            var project = ProjectPaths.Create(_newLocation, _newName, out string error);
            if (project != null) Choose(project);
            else _error = error;
        }
    }

    private void OpenFolder(string folder)
    {
        var project = ProjectPaths.Open(folder, out string error);
        if (project != null)
        {
            Choose(project);
            return;
        }
        _error = error;
        if (_recent.Any(r => string.Equals(r.Path, folder, StringComparison.OrdinalIgnoreCase)) && !Directory.Exists(folder))
        {
            RecentProjects.Remove(folder);
            _recent = RecentProjects.Load();
            _selected = _recent.Count > 0 ? _recent[0].Path : null;
        }
    }

    private void Choose(ProjectPaths project)
    {
        _error = "";
        Chosen = project;
    }

    /// <summary>A folder dropped onto the window opens as a project.</summary>
    public void OnFileDrop(string[] files)
    {
        foreach (var file in files)
        {
            string folder = Directory.Exists(file) ? file : Path.GetDirectoryName(file) ?? file;
            if (string.Equals(Path.GetFileName(file), ProjectPaths.ProjectFileName, StringComparison.OrdinalIgnoreCase) || Directory.Exists(file))
            {
                OpenFolder(folder);
                return;
            }
        }
        _error = "Drop a project folder (it contains infos.json).";
    }
}
