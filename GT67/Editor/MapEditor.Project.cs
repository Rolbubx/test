using GT67.Core;
using GT67.Gui;
using OpenTK.Mathematics;

namespace GT67.Editor;

// The bottom panel, like Unity: the Project browser (shapes, models, prefabs, scripts, shaders and sounds as tiles to
// drag into the scene), the Console (messages and errors) and the API reference for scripts.
public sealed partial class MapEditor
{
    private static readonly string[] BottomTabs = { "Project", "Console", "API" };
    private static readonly IconKind[] BottomIcons = { IconKind.Folder, IconKind.Text, IconKind.Code };
    private const int BottomProject = 0, BottomConsole = 1;
    private const float BottomHeader = 40f, FolderColumn = 170f;

    private enum ProjectFolder { Shapes, Models, Prefabs, Scripts, Shaders, Sounds }

    private static readonly ProjectFolder[] Folders = Enum.GetValues<ProjectFolder>();

    private static IconKind IconFor(ProjectFolder folder) => folder switch
    {
        ProjectFolder.Shapes => IconKind.Cube,
        ProjectFolder.Models => IconKind.Folder,
        ProjectFolder.Prefabs => IconKind.Copy,
        ProjectFolder.Scripts => IconKind.Script,
        ProjectFolder.Shaders => IconKind.Image,
        _ => IconKind.Sound,
    };

    /// <summary>Something in the Project panel: dragged into the viewport (or onto an object, for scripts) it is added to the scene.</summary>
    private sealed record PaletteItem(string Name, string Kind, Func<Mesh>? Make, string? Path);

    private static readonly PaletteItem[] Primitives =
    {
        new("Cube", "primitive", () => Utils.Primitives.Cube(1f), null),
        new("Sphere", "primitive", () => Utils.Primitives.Sphere(0.5f, 32), null),
        new("Plane", "primitive", () => Utils.Primitives.Plane(4f, 4f, 1), null),
        new("Cylinder", "primitive", () => Utils.Primitives.Cylinder(0.5f, 1f, 32), null),
        new("Cone", "primitive", () => Utils.Primitives.Cone(0.5f, 1f, 32), null),
        new("Capsule", "primitive", () => Utils.Primitives.Capsule(0.35f, 1.5f, 24), null),
        new("Torus", "primitive", () => Utils.Primitives.Torus(0.6f, 0.2f, 40, 20), null),
    };

    private int _bottomTab;
    private ProjectFolder _projectFolder = ProjectFolder.Scripts;
    private string? _projectSelection;
    private string _projectFilter = "";

    private List<string> _modelFiles = new(), _soundFiles = new();
    private DateTime _lastFileScan = DateTime.MinValue;

    private string _newScriptName = "MyScript";
    private string _newShaderName = "MyShader";
    private string _apiFilter = "";
    private ApiReference? _api;

    private void DrawBottomPanel(UiRect area, WindowFlags dock)
    {
        var g = G;
        var header = new UiRect(area.X, area.Y, area.Width, BottomHeader);
        var body = new UiRect(area.X, header.Bottom, area.Width, area.Height - BottomHeader);

        if (g.Begin("BottomTabs", header, dock | WindowFlags.NoScroll))
        {
            int errors = _output.Count(o => o.error);
            var labels = new[] { BottomTabs[0], errors > 0 ? $"Console ({errors})" : BottomTabs[1], BottomTabs[2] };
            _bottomTab = g.TabBar("bottomtabs", labels, _bottomTab, BottomIcons, compact: true);
        }
        g.End();

        if (_bottomTab == BottomProject)
        {
            if (g.Begin("ProjectFolders", new UiRect(body.X, body.Y, FolderColumn, body.Height), dock)) DrawProjectFolders();
            g.End();
            if (g.Begin("ProjectFiles", new UiRect(body.X + FolderColumn, body.Y, body.Width - FolderColumn, body.Height), dock)) DrawProjectFiles();
            g.End();
        }
        else if (_bottomTab == BottomConsole)
        {
            if (g.Begin("Console", body, dock | WindowFlags.NoScroll)) DrawConsole();
            g.End();
        }
        else
        {
            if (g.Begin("Api", body, dock)) DrawApi();
            g.End();
        }
    }

    // ---- project browser -------------------------------------------------------------------------

    private void DrawProjectFolders()
    {
        var g = G;
        RefreshFileLists();
        foreach (var folder in Folders)
        {
            int count = ItemsOf(folder).Count;
            if (g.Selectable($"{folder}  {count}##folder{folder}", folder == _projectFolder, IconFor(folder)))
            {
                _projectFolder = folder;
                _projectSelection = null;
            }
        }
    }

    private void DrawProjectFiles()
    {
        var g = G;
        DrawFolderActions();

        var items = ItemsOf(_projectFolder);
        string filter = _projectFilter.Trim();
        if (filter.Length > 0) items = items.Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (items.Count == 0) g.TextDim(EmptyHint(_projectFolder));

        // Tiles flow left to right and wrap at the edge of the panel.
        const float tile = 84f;
        bool first = true;
        foreach (var item in items)
        {
            if (!first)
            {
                if (g.RemainingWidth >= tile + 6f) g.SameLine();
                else g.NewLine();
            }
            first = false;

            bool selected = _projectSelection == item.Name;
            if (g.Tile(item.Name + "##tile" + item.Kind, TileIcon(item), selected, tile)) _projectSelection = item.Name;
            g.Tooltip(TileTooltip(item));
            g.DragSource(item.Name, item);
            if (g.LastItemDoubleClicked) OpenProjectItem(item);
        }

        if (_projectFolder == ProjectFolder.Scripts && Scripts.LastResult is { Success: false } failed)
        {
            g.SeparatorText("Errors");
            foreach (var error in failed.Errors.Take(8)) g.TextStatus(error, true);
        }
        if (_projectFolder == ProjectFolder.Shaders)
            foreach (var shader in _window.Renderer.Custom.Shaders)
                if (shader.Error != null) g.TextStatus($"{shader.Name}: {shader.Error}", true);
    }

    /// <summary>The row above the tiles: search, and creating new scripts and shaders.</summary>
    private void DrawFolderActions()
    {
        var g = G;
        g.SetNextItemWidth(200f);
        g.InputTextBare("##projectfilter", ref _projectFilter, $"Search {_projectFolder}", 200f);

        switch (_projectFolder)
        {
            case ProjectFolder.Scripts when Paths != null:
                g.SameLine();
                g.InputTextBare("##newscript", ref _newScriptName, "Name", 150f);
                g.SameLine();
                if (g.IconTextButton(IconKind.Plus, "New script", 0f, ButtonStyle.Primary)) CreateScript();
                g.SameLine();
                if (g.IconTextButton(IconKind.Refresh, "Recompile")) CompileScripts(announceSuccess: true);
                g.SameLine();
                if (g.IconButton(IconKind.Folder, "openproject")) OpenInEditor(Paths.Scripts);
                g.Tooltip($"Open project in editor ({DescribeEditor()})");
                if (g.RemainingWidth >= 150f) g.SameLine();
                else g.NewLine();
                bool auto = Settings.AutoCompile;
                if (g.Checkbox("Auto-compile", ref auto, g.Theme.ButtonHeight)) Settings.AutoCompile = auto;
                break;

            case ProjectFolder.Shaders when Paths != null:
                g.SameLine();
                g.InputTextBare("##newshader", ref _newShaderName, "Name", 150f);
                g.SameLine();
                if (g.IconTextButton(IconKind.Plus, "New shader", 0f, ButtonStyle.Primary)) CreateShader();
                break;

            case ProjectFolder.Models:
                g.SameLine();
                g.TextDim("Drop .fbx / .gltf / .glb / .obj files onto the window to import them.");
                break;

            case ProjectFolder.Sounds:
                g.SameLine();
                g.TextDim("Drop .wav / .ogg / .mp3 files onto the window to import them.");
                break;
        }
        g.Spacing(4f);
    }

    private List<PaletteItem> ItemsOf(ProjectFolder folder) => folder switch
    {
        ProjectFolder.Shapes => Primitives.ToList(),
        ProjectFolder.Models => _modelFiles.Select(f => new PaletteItem(Path.GetFileNameWithoutExtension(f), "model", null, f)).ToList(),
        ProjectFolder.Prefabs => Prefabs.Names.Select(n => new PaletteItem(n, "prefab", null, n)).ToList(),
        ProjectFolder.Scripts => Scripts.Files.Where(f => !Path.GetFileName(f).StartsWith('_'))
            .Select(f => new PaletteItem(Path.GetFileNameWithoutExtension(f), "script", null, f)).ToList(),
        ProjectFolder.Shaders => _window.Renderer.Custom.Shaders.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new PaletteItem(s.Name, "shader", null, s.File)).ToList(),
        _ => _soundFiles.Select(f => new PaletteItem(Path.GetFileNameWithoutExtension(f), "sound", null, f)).ToList(),
    };

    private static IconKind TileIcon(PaletteItem item) => item.Kind switch
    {
        "primitive" => item.Name == "Sphere" ? IconKind.Sphere : IconKind.Cube,
        "model" => IconKind.Cube,
        "prefab" => IconKind.Copy,
        "script" => IconKind.Script,
        "shader" => IconKind.Image,
        "sound" => IconKind.Sound,
        _ => IconKind.File,
    };

    private static string TileTooltip(PaletteItem item) => item.Kind switch
    {
        "script" => $"{item.Name}.cs\nDouble-click to edit. Drag onto an object (in the scene or the Hierarchy) to attach it.",
        "shader" => $"{item.Name}.shader\nDouble-click to edit. Pick it for an object in the Inspector > Shader.",
        "sound" => $"{Path.GetFileName(item.Path)}\nDrag into the scene to place it. Double-click to add it in front of the camera.",
        "prefab" => $"Prefab '{item.Name}'\nDrag into the scene, or double-click to add it in front of the camera.\nScripts spawn it with Game.SpawnPrefab(\"{item.Name}\", position).",
        _ => $"{item.Name}\nDrag into the scene, or double-click to add it in front of the camera.",
    };

    private static string EmptyHint(ProjectFolder folder) => folder switch
    {
        ProjectFolder.Models => "No models in Assets/Models yet.",
        ProjectFolder.Prefabs => "No prefabs yet: select an object and press 'Save as prefab' in the Inspector.",
        ProjectFolder.Scripts => "No scripts yet: type a name and press New script.",
        ProjectFolder.Shaders => "No shaders yet: type a name and press New shader.",
        ProjectFolder.Sounds => "No sounds in Assets/Sounds yet.",
        _ => "Nothing here.",
    };

    /// <summary>Double-click on a tile: scripts and shaders open in the code editor, everything else is added in front of the camera.</summary>
    private void OpenProjectItem(PaletteItem item)
    {
        var front = _window.Camera.Position + _window.Camera.Front * 6f;
        switch (item.Kind)
        {
            case "script":
            case "shader":
                OpenInEditor(item.Path!);
                break;
            case "sound":
                ImportSound(item.Path!, front);
                break;
            default:
                Spawn(item, front);
                break;
        }
    }

    private void RefreshFileLists()
    {
        if ((DateTime.Now - _lastFileScan).TotalSeconds < 3) return;
        _lastFileScan = DateTime.Now;

        string root = Paths?.Root ?? AppContext.BaseDirectory;
        string models = Paths?.Models ?? Path.Combine(root, "Assets", "Models");
        _modelFiles = Scan(models, ModelImporter.IsModelFile);
        _soundFiles = Scan(Path.Combine(root, "Assets", "Sounds"), AudioClip.IsAudioFile);

        if (Paths != null) _window.Renderer.Custom.AddDirectory(Path.Combine(Paths.Root, "Shaders", "Custom"), first: true);

        static List<string> Scan(string folder, Func<string, bool> accept) => Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories).Where(accept)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();
    }

    private void CreateScript()
    {
        var created = Scripts.CreateScript(_newScriptName, out var error);
        if (created == null)
        {
            Say(error, error: true);
            return;
        }
        Say($"Created {Path.GetFileName(created)}.");
        _projectSelection = Path.GetFileNameWithoutExtension(created);
        OpenInEditor(created);
        CompileScripts(announceSuccess: true);
    }

    private void CreateShader()
    {
        string folder = Path.Combine(Paths!.Root, "Shaders", "Custom");
        string name = new string(_newShaderName.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        string path = Path.Combine(folder, name + ShaderLibrary.Extension);
        if (name.Length == 0 || char.IsDigit(name[0])) Say("Use letters, digits and _ (not starting with a digit).", error: true);
        else if (File.Exists(path)) Say($"{name}{ShaderLibrary.Extension} already exists.", error: true);
        else
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, ShaderLibrary.Template(name));
            _window.Renderer.Custom.AddDirectory(folder, first: true);
            Say($"Created {name}{ShaderLibrary.Extension}.");
            _projectSelection = name;
            OpenInEditor(path);
        }
    }

    // ---- dropping project items into the scene ---------------------------------------------------

    /// <summary>A project item released over the viewport (not over a panel): added where the mouse points, or attached to the object under it (scripts).</summary>
    private void HandlePaletteDrop()
    {
        if (!G.TryGetDrop(out var payload, out bool overUi) || payload is not PaletteItem item) return;
        if (overUi || !_window.Viewport.Contains(_window.MousePosition)) return;

        switch (item.Kind)
        {
            case "script":
                var ray = _window.GetMouseRay();
                var target = Scene.Raycast(ray.Origin, ray.Direction);
                if (target is { EditorOnly: false }) AttachScript(target, item);
                else Say($"Drop {item.Name} onto an object to attach it.", error: true);
                break;
            case "shader":
                Say("Shaders are picked per object in the Inspector > Shader.", error: true);
                break;
            case "sound":
                ImportSound(item.Path!, _window.GetMouseWorldPosition());
                break;
            default:
                Spawn(item, _window.GetMouseWorldPosition());
                break;
        }
    }

    /// <summary>Attaches the script class of a Scripts/*.cs file to an object (like dropping a script on an object in Unity).</summary>
    private void AttachScript(GameObject obj, PaletteItem script)
    {
        if (Playing)
        {
            Say("Stop playing to attach scripts.", error: true);
            return;
        }
        var type = Scripts.Types.FirstOrDefault(t => t.Name == script.Name);
        if (type == null)
        {
            Say($"No compiled script class called {script.Name} (check {script.Name}.cs and the Console).", error: true);
            return;
        }
        obj.AddScript(type);
        Selected = obj;
        Say($"Attached {type.Name} to '{obj.Name}'.");
    }

    // ---- console ---------------------------------------------------------------------------------

    private void DrawConsole()
    {
        var g = G;
        if (g.IconTextButton(IconKind.Trash, "Clear", 100f)) _output.Clear();
        g.SameLine();
        g.TextDim($"{_output.Count} message(s), {_output.Count(o => o.error)} error(s)");

        g.BeginChild("outputlist", g.AvailableHeight - 4f, border: true);
        foreach (var (text, error) in _output)
        {
            if (error) g.TextStatus(text, true);
            else g.TextWrapped(text);
        }
        if (_scrollOutput)
        {
            g.ScrollToEnd();
            _scrollOutput = false;
        }
        g.EndChild();
    }

    // ---- API reference ---------------------------------------------------------------------------

    /// <summary>The scripting API as a searchable list, with the descriptions from the source comments.</summary>
    private void DrawApi()
    {
        var g = G;
        _api ??= new ApiReference();
        g.InputTextBare("##apifilter", ref _apiFilter, "Search the API: rain, sound, position, LoadScene...", 420f);
        g.Spacing(2f);

        bool filtering = !string.IsNullOrWhiteSpace(_apiFilter);
        foreach (var type in _api.Search(_apiFilter))
        {
            if (!g.CollapsingHeader($"{type.Name}##api{type.Name}", defaultOpen: filtering)) continue;
            if (type.Summary.Length > 0) g.TextWrapped(type.Summary, dim: true);
            foreach (var member in type.Members.Take(filtering ? 60 : 200))
            {
                g.TextWrapped(member.Signature);
                if (member.Summary.Length > 0) g.TextWrapped("   " + member.Summary, dim: true);
            }
        }
    }

    // ---- the external code editor ----------------------------------------------------------------

    private string DescribeEditor() => Settings.ExternalEditor.Trim().ToLowerInvariant() switch
    {
        "" or "default" => "your default editor",
        "code" => "Visual Studio Code",
        "code-insiders" => "Visual Studio Code Insiders",
        "rider" => "Rider",
        _ => Settings.ExternalEditor,
    };

    private void OpenInEditor(string path)
    {
        if (Paths == null) return;
        if (!ExternalEditor.Open(Settings.ExternalEditor, Paths.Root, path, out var message)) Say(message, error: true);
        else Say(message);
    }
}
