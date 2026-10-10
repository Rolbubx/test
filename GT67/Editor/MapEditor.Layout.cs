using GT67.Core;
using GT67.Gui;
using GT67.UI;
using OpenTK.Mathematics;
using SkiaSharp;

namespace GT67.Editor;

public sealed partial class MapEditor
{
    private const float ToolbarHeight = 52f;
    private const float SplitterThickness = 6f;

    private static readonly string[] LeftTabs = { "Scene", "Assets", "Code", "UI" };
    private static readonly IconKind[] LeftIcons = { IconKind.Cube, IconKind.Folder, IconKind.Code, IconKind.Layout };
    private static readonly string[] RightTabs = { "Inspector", "Lighting", "Water", "Render", "Terrain", "UI" };
    private static readonly IconKind[] RightIcons = { IconKind.Select, IconKind.Light, IconKind.Water, IconKind.Camera, IconKind.Terrain, IconKind.Layout };

    private const int LeftUi = 3;
    private const int RightInspector = 0, RightTerrain = 4, RightUi = 5;

    private float _leftWidth = 290f, _rightWidth = 360f, _bottomHeight = 190f;
    private int _leftTab, _rightTab;
    private EditorTool _lastTool = EditorTool.Select;

    /// <summary>True while the UI designer is in front: the viewport edits the game UI instead of the 3D scene.</summary>
    private bool UiMode => _leftTab == LeftUi || _rightTab == RightUi;

    // ---- the whole layout ------------------------------------------------------------------------

    public void Draw(float deltaTime)
    {
        _window.DebugUI.Draw(deltaTime); // keeps the FPS smoothing running; the debug window itself stays hidden here
        WatchScripts();

        var g = G;
        var size = g.DisplaySize;
        _leftWidth = Math.Clamp(_leftWidth, 220f, MathF.Max(220f, size.X * 0.4f));
        _rightWidth = Math.Clamp(_rightWidth, 260f, MathF.Max(260f, size.X * 0.45f));
        _bottomHeight = Math.Clamp(_bottomHeight, 90f, MathF.Max(90f, size.Y * 0.5f));

        // Switching tools to the terrain brush shows its panel, and the other way round.
        if (Tool != _lastTool)
        {
            if (Tool == EditorTool.Terrain) _rightTab = RightTerrain;
            else if (_rightTab == RightTerrain) _rightTab = RightInspector;
            _lastTool = Tool;
        }
        if (SelectedLight != null && _rightTab == RightUi && !UiMode) _rightTab = RightInspector;

        float bodyTop = ToolbarHeight;
        float bodyHeight = size.Y - bodyTop;
        var left = new UiRect(0f, bodyTop, _leftWidth, bodyHeight);
        var right = new UiRect(size.X - _rightWidth, bodyTop, _rightWidth, bodyHeight);
        var bottom = new UiRect(left.Right, size.Y - _bottomHeight, size.X - _leftWidth - _rightWidth, _bottomHeight);
        _window.Viewport = new UiRect(left.Right, bodyTop, bottom.Width, bodyHeight - _bottomHeight);

        const WindowFlags dock = WindowFlags.Fixed | WindowFlags.Panel;
        if (g.Begin("Toolbar", new UiRect(0f, 0f, size.X, ToolbarHeight), dock | WindowFlags.NoScroll)) DrawToolbar();
        g.End();

        if (g.Begin("Left", left, dock))
        {
            _leftTab = g.TabBar("lefttabs", LeftTabs, _leftTab, LeftIcons, compact: true);
            g.Spacing(4f);
            DrawLeftTab();
        }
        g.End();

        if (g.Begin("Right", right, dock))
        {
            _rightTab = g.TabBar("righttabs", RightTabs, _rightTab, RightIcons, compact: true);
            g.Spacing(4f);
            DrawRightTab();
        }
        g.End();

        if (g.Begin("Bottom", bottom, dock | WindowFlags.NoScroll)) DrawOutput();
        g.End();

        g.Splitter("left", new UiRect(left.Right - SplitterThickness * 0.5f, bodyTop, SplitterThickness, bodyHeight), true, ref _leftWidth, 220f, size.X * 0.4f, 1f);
        g.Splitter("right", new UiRect(right.X - SplitterThickness * 0.5f, bodyTop, SplitterThickness, bodyHeight), true, ref _rightWidth, 260f, size.X * 0.45f, -1f);
        g.Splitter("bottom", new UiRect(bottom.X, bottom.Y - SplitterThickness * 0.5f, bottom.Width, SplitterThickness), false, ref _bottomHeight, 90f, size.Y * 0.5f, -1f);

        DrawViewportOverlay();
        DrawUiSelection();
        HandlePaletteDrop();
    }

    // ---- toolbar ---------------------------------------------------------------------------------

    private void DrawToolbar()
    {
        var g = G;
        g.Spacing(2f);

        if (Playing)
        {
            if (g.IconTextButton(IconKind.Stop, "Stop", 96f, ButtonStyle.Danger)) Stop();
        }
        else if (g.IconTextButton(IconKind.Play, "Play", 96f, ButtonStyle.Primary)) Play();
        g.Tooltip("Run the scripts and the game UI. Stop restores the scene.");

        g.SameLine();
        if (Playing)
        {
            var game = _window.Game;
            if (g.IconTextButton(game.ViewDetached ? IconKind.Camera : IconKind.Eye, game.ViewDetached ? "Player view" : "Free view", 120f))
                game.ViewDetached = !game.ViewDetached;
            g.Tooltip(game.ViewDetached
                ? "Back to the game camera (what the player sees)."
                : "Detach the view from the game camera: fly around with the right mouse button + WASD while the game runs.");
        }
        else
        {
            if (g.IconTextButton(IconKind.Compile, "Compile", 120f)) Compile();
            g.Tooltip("Compile the scripts, then write the game into Generated/GeneratedGame/ (several files, with the models, textures and sounds it needs).\nRun it with: dotnet run -- --generated");
        }

        g.SameLine();
        g.VerticalSeparator();

        ToolButton(IconKind.Select, "Select (Q)", EditorTool.Select);
        ToolButton(IconKind.Move, "Move (W)", EditorTool.Move);
        ToolButton(IconKind.Rotate, "Rotate (E)", EditorTool.Rotate);
        ToolButton(IconKind.Scale, "Scale (R)", EditorTool.Scale);
        ToolButton(IconKind.Terrain, "Terrain brush (T)", EditorTool.Terrain);

        g.SameLine();
        g.VerticalSeparator();

        g.SameLine();
        if (g.Button(Space == GizmoSpace.World ? "World" : "Local", 70f)) Space = Space == GizmoSpace.World ? GizmoSpace.Local : GizmoSpace.World;
        g.Tooltip("Gizmo orientation: world axes or the object's own axes.");

        g.SameLine();
        bool snap = Settings.SnapEnabled;
        if (g.Checkbox("Snap", ref snap, g.Theme.ButtonHeight))
        {
            Settings.SnapEnabled = snap;
            ApplySnapSettings();
        }
        g.Tooltip("Snap moves, rotations and scales to the steps set in Settings.");

        g.SameLine();
        if (g.IconButton(IconKind.Gear, "settings")) g.TogglePopup("##settings", g.LastItemRect);
        g.Tooltip("Editor settings");
        DrawSettingsPopup();

        g.SameLine();
        string status = SelectedLight != null ? $"Light: {SelectedLight.Name}"
            : Selected != null ? $"Selected: {Selected.Name}"
            : SelectedUi != null ? $"UI: {SelectedUi.Name}"
            : VolumeSelected ? "Main Volume" : "Nothing selected";
        var statusRect = g.Reserve(0f, g.Theme.ButtonHeight);
        g.LabelRect(statusRect.Deflate(8f, 0f), status, g.Theme.TextDim, g.Theme.FontSize, FontWeight.Regular, TextAlign.Left);
    }

    private void ToolButton(IconKind icon, string tip, EditorTool tool)
    {
        var g = G;
        g.SameLine();
        if (g.IconButton(icon, tip, Tool == tool))
        {
            Tool = tool;
            if (UiMode) _leftTab = 0; // the 3D tools only make sense on the scene
            if (UiMode) _rightTab = RightInspector;
        }
        g.Tooltip(tip);
    }

    private void DrawSettingsPopup()
    {
        var g = G;
        if (!g.BeginPopup("##settings", 330f)) return;

        g.Heading("Editor settings");
        string editor = Settings.ExternalEditor;
        if (g.InputText("Script editor", ref editor, "code")) Settings.ExternalEditor = editor;
        g.TextWrapped("The program that opens scripts: code (Visual Studio Code), rider, notepad... or default for the system's own choice.", dim: true);
        bool autoCompile = Settings.AutoCompile;
        if (g.Checkbox("Auto-compile on save", ref autoCompile)) Settings.AutoCompile = autoCompile;

        g.SeparatorText("Snapping");
        g.Drag("Move step", Settings.MoveSnap, 0.05f, v => { Settings.MoveSnap = v; ApplySnapSettings(); }, 0.01f, 100f);
        g.Drag("Rotate step", Settings.RotateSnap, 0.5f, v => { Settings.RotateSnap = v; ApplySnapSettings(); }, 1f, 180f);
        g.Drag("Scale step", Settings.ScaleSnap, 0.01f, v => { Settings.ScaleSnap = v; ApplySnapSettings(); }, 0.01f, 10f);

        g.SeparatorText("Debug");
        bool debugFlag = Settings.DebugFlag;
        if (g.Checkbox("debug_flag", ref debugFlag)) Settings.DebugFlag = debugFlag;
        g.TextWrapped("Temporary switch for testing: read it in code with DebugFlag.On (GT67.Utils).", dim: true);
        g.EndPopup();
    }

    // ---- viewport overlay ------------------------------------------------------------------------

    private void DrawViewportOverlay()
    {
        var g = G;
        var vp = _window.Viewport;
        const WindowFlags flags = WindowFlags.Fixed | WindowFlags.NoBackground | WindowFlags.NoScroll | WindowFlags.ClickThrough;
        if (g.Begin("##vpoverlay", vp, flags))
        {
            string hint = Playing ? "PLAYING" : UiMode ? "UI designer: click to select, drag to move, corner to resize"
                : Tool == EditorTool.Terrain ? "Terrain brush: left-drag to sculpt" : "RMB + WASD fly   F focus   Del delete   Ctrl+D duplicate";
            var chip = new UiRect(vp.X + 12f, vp.Bottom - 38f, g.MeasureText(hint, 12f, FontWeight.Medium) + 24f, 26f);
            g.PanelRect(chip, Playing ? new SKColor(220, 70, 70, 230) : new SKColor(10, 12, 16, 170), 13f, SKColors.Transparent);
            g.LabelRect(chip, hint, SKColors.White, 12f, FontWeight.Medium, TextAlign.Center);
        }
        g.End();
    }

    // ---- left dock -------------------------------------------------------------------------------

    private void DrawLeftTab()
    {
        switch (_leftTab)
        {
            case 0: DrawSceneTab(); break;
            case 1: DrawAssets(); break;
            case 2: DrawCode(); break;
            default: DrawUiTree(); break;
        }
    }

    private void DrawSceneTab()
    {
        var g = G;
        g.SeparatorText("Global");
        if (g.Selectable("Main Volume", VolumeSelected, IconKind.Camera)) SelectVolume();
        g.Tooltip("Post-processing: tone mapping, bloom, depth of field, colour correction, sharpening...");

        g.SeparatorText("Objects");
        foreach (var root in Scene.Roots.ToArray()) DrawSceneNode(root);

        g.SeparatorText("Lights");
        if (Scene.PointLights.Count == 0 && Scene.Spot == null) g.TextDim("No point lights.");
        foreach (var light in Scene.PointLights.ToArray())
            if (g.Selectable($"{light.Name}##pl{Scene.PointLights.IndexOf(light)}", SelectedLight == light, IconKind.Light)) SelectLight(light);
        if (Scene.Spot is { } spot && g.Selectable("Spot light", SelectedLight == spot, IconKind.Light)) SelectLight(spot);
        if (Scene.Waters.Count > 0)
        {
            g.SeparatorText("Water");
            foreach (var water in Scene.Waters)
            {
                if (g.Selectable(water.Name + "##w" + water.GetHashCode(), water == SelectedWater, IconKind.Water)) SelectWater(water);
                if (g.LastItemDoubleClicked && water == SelectedWater) FocusSelection();
            }
        }
    }

    private void DrawSceneNode(GameObject obj)
    {
        if (obj.EditorOnly) return;
        var g = G;
        bool open = g.TreeNode($"{obj.Name}##{obj.Id}", obj == Selected, out bool clicked, obj.Children.Count == 0,
            obj.Children.Count > 0 ? IconKind.Folder : Scene.Terrains.Any(t => t.Object == obj) ? IconKind.Terrain : IconKind.Cube, defaultOpen: true);
        if (clicked) Selected = obj;
        if (g.LastItemDoubleClicked && obj == Selected) FocusSelection();
        if (!open) return;

        foreach (var child in obj.Children.ToArray()) DrawSceneNode(child);
        g.TreePop();
    }

    // ---- assets ----------------------------------------------------------------------------------

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

    private List<string> _modelFiles = new();
    private DateTime _lastModelScan = DateTime.MinValue;
    private int _assetTab;

    private void DrawAssets()
    {
        var g = G;
        _assetTab = g.TabBar("assettabs", new[] { "Shapes", "Models", "Prefabs", "World" }, _assetTab);
        g.Spacing(4f);

        switch (_assetTab)
        {
            case 0:
                g.TextWrapped("Click to add in front of the camera, or drag into the scene.", dim: true);
                g.Spacing(2f);
                bool first = true;
                foreach (var item in Primitives) PaletteButton(item, ref first);
                break;

            case 1:
                g.TextWrapped("Drop .fbx / .gltf / .glb / .obj files from your file manager onto the window: they load from " +
                              "where they are (textures included) and are copied into Assets/Models on Compile.", dim: true);
                g.Spacing(2f);
                RefreshModelList();
                if (_modelFiles.Count == 0) g.TextDim("No models in Assets/Models yet.");
                first = true;
                foreach (var file in _modelFiles)
                    PaletteButton(new PaletteItem(Path.GetFileNameWithoutExtension(file), "model", null, file), ref first);
                break;

            case 2:
                g.TextWrapped("Reusable objects: select something in the scene and press 'Save as prefab' in the Inspector. " +
                              "Click a prefab to add it in front of the camera, or drag it into the scene. Scripts spawn " +
                              "them with Game.SpawnPrefab(\"Name\", position).", dim: true);
                g.Spacing(2f);
                if (Prefabs.Names.Count == 0) g.TextDim("No prefabs yet (they are saved in Prefabs/).");
                first = true;
                foreach (var name in Prefabs.Names) PaletteButton(new PaletteItem(name, "prefab", null, name), ref first);
                break;

            default:
                if (g.IconTextButton(IconKind.Sphere, "Player (capsule)")) AddPlayer();
                g.Tooltip("A capsule tagged 'Player' with the PlayerController script: WASD/arrows walk, Space jumps.\nAdds a game camera too if the scene has none.");
                if (g.IconTextButton(IconKind.Camera, "Camera")) AddCamera();
                g.Tooltip("The game camera: what the player sees when you press Play. Follows the object tagged 'Player'\n(third person, first person or top-down), or stays where you put it (Fixed). Settings in the Inspector.");
                if (g.IconTextButton(IconKind.Light, "Point light")) AddPointLight();
                if (g.IconTextButton(IconKind.Water, "Water (lake)")) AddWater();
                g.DragSource("Water", new PaletteItem("Water", "water", null, null));
                g.Tooltip("Click: a lake in front of the camera. Or drag it into the viewport to place it.\n" +
                          "On a terrain it digs its own basin. Then move it with W and resize it with R.");
                if (g.IconTextButton(IconKind.Terrain, "Terrain...")) Tool = EditorTool.Terrain;
                g.Spacing(4f);
                g.TextWrapped("Lights are edited in the Inspector and the Lighting tab; water in the Water tab. " +
                              "The player and camera are driven by Scripts/PlayerController.cs and Scripts/GameCamera.cs: " +
                              "tune them in the Inspector or edit the code.", dim: true);
                break;
        }
    }

    /// <summary>Palette buttons flow left to right and wrap at the edge of the panel.</summary>
    private void PaletteButton(PaletteItem item, ref bool first)
    {
        var g = G;
        float width = g.MeasureText(item.Name, 0f, FontWeight.Medium) + 28f;
        if (!first)
        {
            if (g.RemainingWidth >= width) g.SameLine();
            else g.NewLine();
        }
        first = false;

        if (g.Button(item.Name, width)) Spawn(item, _window.Camera.Position + _window.Camera.Front * 6f);
        g.DragSource(item.Name, item);
    }

    /// <summary>A palette item released over the viewport (not over a panel) is spawned where the mouse points.</summary>
    private void HandlePaletteDrop()
    {
        if (!G.TryGetDrop(out var payload, out bool overUi) || payload is not PaletteItem item) return;
        if (overUi || !_window.Viewport.Contains(_window.MousePosition)) return;
        Spawn(item, _window.GetMouseWorldPosition());
    }

    private void RefreshModelList()
    {
        if ((DateTime.Now - _lastModelScan).TotalSeconds < 3) return;
        _lastModelScan = DateTime.Now;

        string root = Paths?.Models ?? Path.Combine(AppContext.BaseDirectory, "Assets", "Models");
        _modelFiles = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories).Where(ModelImporter.IsModelFile)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();
    }

    // ---- scripts ---------------------------------------------------------------------------------

    private string _newScriptName = "MyScript";
    private string? _selectedScript;

    // ---- the Code tab: C# scripts, shaders and the API reference ---------------------------------

    private int _codeTab;
    private string _newShaderName = "MyShader";
    private string _apiFilter = "";
    private ApiReference? _api;

    private void DrawCode()
    {
        var g = G;
        _codeTab = g.TabBar("codetabs", new[] { "Scripts", "Shaders", "API" }, _codeTab);
        g.Spacing(2f);
        switch (_codeTab)
        {
            case 0: DrawScripts(); break;
            case 1: DrawShaders(); break;
            default: DrawApi(); break;
        }
    }

    /// <summary>Custom shaders: .shader files in Shaders/Custom, written in the small GT67 shader language.</summary>
    private void DrawShaders()
    {
        var g = G;
        if (Paths == null)
        {
            g.TextStatus("Project folder not found: shaders cannot be edited.", true);
            return;
        }

        var library = _window.Renderer.Custom;
        string folder = Path.Combine(Paths.Root, "Shaders", "Custom");
        library.AddDirectory(folder, first: true);

        g.TextWrapped("A shader is a few lines in a .shader file: 'param' lines you can change per object, and a 'surface' block " +
                      "(GLSL) that sets albedo, emission, shininess and specular. It reloads when you save. " +
                      "Pick it for an object in the Inspector > Shader.", dim: true);
        g.Spacing(2f);

        g.SeparatorText("New shader");
        g.InputTextBare("##newshader", ref _newShaderName, "Name");
        if (g.IconTextButton(IconKind.Plus, "Create and open", 0f, ButtonStyle.Primary))
        {
            string name = new string(_newShaderName.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
            string path = Path.Combine(folder, name + ShaderLibrary.Extension);
            if (name.Length == 0 || char.IsDigit(name[0])) Say("Use letters, digits and _ (not starting with a digit).", error: true);
            else if (File.Exists(path)) Say($"{name}{ShaderLibrary.Extension} already exists.", error: true);
            else
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(path, ShaderLibrary.Template(name));
                Say($"Created {name}{ShaderLibrary.Extension}.");
                OpenInEditor(path);
            }
        }

        g.SeparatorText("Shaders");
        foreach (var shader in library.Shaders.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (g.Selectable(shader.Name + "##shader", false, IconKind.File)) { }
            if (g.LastItemDoubleClicked) OpenInEditor(shader.File);
            if (shader.Error != null) g.TextStatus(shader.Error, true);
        }
        if (!library.Shaders.Any()) g.TextDim("No shaders yet.");
        else g.TextDim("Double-click a shader to open it.");
    }

    /// <summary>The scripting API as a searchable list, with the descriptions from the source comments.</summary>
    private void DrawApi()
    {
        var g = G;
        _api ??= new ApiReference();
        g.TextWrapped("What your C# scripts can use. Search for a word like 'rain', 'sound' or 'position'.", dim: true);
        g.InputTextBare("##apifilter", ref _apiFilter, "Search the API");
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

    private void DrawScripts()
    {
        var g = G;
        if (Paths == null)
        {
            g.TextStatus("Project folder not found: scripts cannot be edited.", true);
            return;
        }

        g.TextWrapped($"Scripts are edited in {DescribeEditor()} and recompile when you save them.", dim: true);
        g.Spacing(2f);
        if (g.IconTextButton(IconKind.Folder, "Open project in editor"))
            OpenInEditor(Paths.Scripts);
        g.Spacing(2f);

        g.SeparatorText("New script");
        g.InputTextBare("##newscript", ref _newScriptName, "Name");
        if (g.IconTextButton(IconKind.Plus, "Create and open", 0f, ButtonStyle.Primary))
        {
            var created = Scripts.CreateScript(_newScriptName, out var error);
            if (created == null) Say(error, error: true);
            else
            {
                Say($"Created {Path.GetFileName(created)}.");
                _selectedScript = created;
                OpenInEditor(created);
                CompileScripts(announceSuccess: true);
            }
        }

        g.SeparatorText("Files");
        foreach (var file in Scripts.Files)
        {
            string name = Path.GetFileName(file);
            if (name.StartsWith('_')) continue;
            if (g.Selectable(name + "##script", file == _selectedScript, IconKind.File)) _selectedScript = file;
            if (g.LastItemDoubleClicked) OpenInEditor(file);
        }
        g.TextDim("Double-click a file to open it.");

        g.Spacing(4f);
        if (g.IconTextButton(IconKind.Refresh, "Recompile now")) CompileScripts(announceSuccess: true);
        bool auto = Settings.AutoCompile;
        if (g.Checkbox("Auto-compile on save", ref auto)) Settings.AutoCompile = auto;

        if (Scripts.LastResult is { Success: false } failed)
        {
            g.SeparatorText("Errors");
            foreach (var error in failed.Errors.Take(8)) g.TextStatus(error, true);
        }
    }

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

    // ---- output ----------------------------------------------------------------------------------

    private void DrawOutput()
    {
        var g = G;
        g.Heading("Output", 70f);
        g.SameLine();
        if (g.IconTextButton(IconKind.Trash, "Clear", 90f)) _output.Clear();

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

    // ---- inspector -------------------------------------------------------------------------------

    private void DrawRightTab()
    {
        var g = G;
        switch (_rightTab)
        {
            case 0:
                if (VolumeSelected)
                {
                    g.Heading("Main Volume");
                    g.TextDim("Post-processing applied to the whole view");
                    _window.DebugUI.DrawVolume();
                }
                else if (SelectedLight != null) DrawLightInspector(SelectedLight);
                else if (SelectedWater != null) DrawWaterInspector(SelectedWater);
                else _window.DebugUI.DrawInspector();
                break;
            case 1:
                if (g.IconTextButton(IconKind.Plus, "Add point light")) AddPointLight();
                _window.DebugUI.DrawLighting();
                break;
            case 2:
                if (g.IconTextButton(IconKind.Plus, "Add water")) AddWater();
                _window.DebugUI.DrawWater();
                break;
            case 3:
                _window.DebugUI.DrawRender();
                if (g.Button("Edit Main Volume...")) SelectVolume();
                break;
            case RightTerrain:
                DrawTerrainPanel();
                break;
            default:
                DrawUiProperties();
                break;
        }
    }

    private void DrawWaterInspector(Water water)
    {
        var g = G;
        g.Heading(water.Name);
        string name = water.Name;
        if (g.InputText("Name", ref name, "Water") && name.Trim().Length > 0) water.Name = name.Trim();
        g.TextWrapped("Move it with the Move tool (W; the up arrow changes the level), resize it with the Scale tool (R), " +
                      "or type the numbers below.", dim: true);

        if (TerrainUnder(water.Position) != null)
        {
            if (g.IconTextButton(IconKind.Terrain, "Dig basin")) DigBasin(water);
            g.Tooltip("Lower the terrain under this water into a bowl 2.5 m below its level (the shape follows its size).");
            g.SameLine();
        }
        if (g.IconTextButton(IconKind.Trash, "Delete", 0f, ButtonStyle.Danger))
        {
            DeleteSelection();
            return;
        }

        _window.DebugUI.DrawWaterSettings(water);
    }

    private void DrawLightInspector(Light light)
    {
        var g = G;
        g.Heading(light.Name);
        g.TextDim(light is SpotLight ? "Spot light" : "Point light");
        g.Input("Name", light.Name, v => light.Name = v);
        g.Check("Enabled", light.Enabled, v => light.Enabled = v);
        g.Rgb("Colour", light.Color, v => light.Color = v);
        g.Slider("Intensity", light.Intensity, 0f, 10f, v => light.Intensity = v);

        switch (light)
        {
            case PointLight point:
                g.Drag3("Position", point.Position, 0.05f, v => point.Position = v);
                g.Slider("Range", point.Range, 1f, 60f, v => point.Range = v, "0.0");
                break;
            case SpotLight spot:
                g.Drag3("Position", spot.Position, 0.05f, v => spot.Position = v);
                g.Drag3("Direction", spot.Direction, 0.01f, v => spot.Direction = v);
                g.Slider("Inner angle", spot.InnerAngle, 1f, 80f, v => spot.InnerAngle = v, "0");
                g.Slider("Outer angle", spot.OuterAngle, 1f, 89f, v => spot.OuterAngle = v, "0");
                g.Slider("Range", spot.Range, 1f, 80f, v => spot.Range = v, "0.0");
                break;
        }

        g.Spacing(4f);
        if (light is PointLight p && g.IconTextButton(IconKind.Trash, "Delete light", 0f, ButtonStyle.Danger))
        {
            Scene.PointLights.Remove(p);
            SelectLight(null);
        }
        g.TextWrapped("Drag the light in the viewport with the Move tool (W).", dim: true);
    }

    // The name typed for "Save as prefab", reset when another object is selected.
    private string _prefabName = "";
    private GameObject? _prefabNameFor;

    /// <summary>Called from the inspector for the selected object: prefab, collision, model source, scripts.</summary>
    public void DrawInspectorExtras(GameObject obj)
    {
        var g = G;
        DrawPrefabSection(obj);

        g.SeparatorText("Collision");
        g.Choose("Mode", obj.Collision, v => obj.Collision = v);
        if (obj.Model.SourcePath != null) g.TextDim($"Imported from {Path.GetFileName(obj.Model.SourcePath)}");

        g.SeparatorText("Scripts");
        for (int i = 0; i < obj.Scripts.Count; i++)
        {
            var script = obj.Scripts[i];
            g.PushId(i);
            bool open = g.CollapsingHeader(script.GetType().Name + "##script", defaultOpen: true);
            if (open)
            {
                ParameterEditor.Draw(g, script);
                if (g.IconTextButton(IconKind.Code, "Edit", 90f)) OpenScriptSource(script.GetType());
                g.SameLine();
                if (g.IconTextButton(IconKind.Trash, "Remove", 100f, ButtonStyle.Danger))
                {
                    obj.RemoveScript(script);
                    g.PopId();
                    break;
                }
            }
            g.PopId();
        }

        if (Scripts.Types.Count == 0)
        {
            g.TextDim("No scripts compiled yet (see the Scripts tab).");
        }
        else
        {
            var names = Scripts.Types.Select(t => t.Name).ToList();
            int choice = -1;
            if (g.Combo("Add script", ref choice, names) && choice >= 0)
            {
                obj.AddScript(Scripts.Types[choice]);
                Say($"Attached {names[choice]} to '{obj.Name}'.");
            }
        }

        g.Spacing(2f);
        if (g.IconTextButton(IconKind.ArrowDown, "Drop to ground")) DropToGround(obj);
    }

    /// <summary>Save as prefab; for an instance also Apply (update the prefab and its other copies), Revert and Unlink.</summary>
    private void DrawPrefabSection(GameObject obj)
    {
        var g = G;
        g.SeparatorText("Prefab");
        if (Playing)
        {
            g.TextDim(obj.PrefabName != null ? $"Instance of '{obj.PrefabName}'. Stop playing to change prefabs." : "Stop playing to save prefabs.");
            return;
        }
        if (_prefabNameFor != obj)
        {
            _prefabNameFor = obj;
            _prefabName = obj.PrefabName ?? PrefabLibrary.CleanName(obj.Name);
        }

        if (obj.PrefabName is { } linked)
        {
            bool exists = Prefab.Exists(linked);
            g.TextDim(exists ? $"Instance of '{linked}'." : $"Instance of '{linked}' (its file is missing).");

            if (g.IconTextButton(IconKind.Check, "Apply", 90f, ButtonStyle.Primary)) ApplyToPrefab(obj, linked);
            g.Tooltip("Save this object into the prefab, then update every other copy of it in the scene\n(they keep their own name, place and rotation).");
            g.SameLine();
            if (exists && g.IconTextButton(IconKind.Refresh, "Revert", 90f))
            {
                var fresh = PrefabLibrary.Rebuild(Scene, obj);
                if (fresh != null)
                {
                    Selected = fresh;
                    Say($"'{fresh.Name}' reverted to the prefab '{linked}'.");
                }
            }
            if (exists) g.Tooltip("Throw away the changes made to this copy: rebuild it from the prefab.");
            g.SameLine();
            if (g.IconTextButton(IconKind.Close, "Unlink", 90f))
            {
                obj.PrefabName = null;
                Say($"'{obj.Name}' is no longer linked to the prefab '{linked}'.");
            }
            g.Tooltip("Keep this object as it is, but stop treating it as a copy of the prefab.");
            g.Spacing(2f);
        }

        g.InputTextBare("##prefabname", ref _prefabName, "Prefab name");
        string clean = PrefabLibrary.CleanName(_prefabName);
        bool replaces = clean.Length > 0 && Prefab.Exists(clean) && !string.Equals(clean, obj.PrefabName, StringComparison.OrdinalIgnoreCase);
        if (g.IconTextButton(IconKind.Plus, replaces ? "Replace prefab" : "Save as prefab", 0f, obj.PrefabName == null ? ButtonStyle.Primary : ButtonStyle.Normal))
        {
            if (string.Equals(clean, obj.PrefabName, StringComparison.OrdinalIgnoreCase)) ApplyToPrefab(obj, clean);
            else SaveAsPrefab(obj, clean);
        }
        g.Tooltip("Save this object, its children and scripts as Prefabs/<name>.prefab, to place copies from Assets > Prefabs\nor spawn them from scripts with Game.SpawnPrefab(\"name\", position).");
        if (replaces) g.TextStatus($"A prefab called '{clean}' exists: it will be replaced.", true);
    }

    private bool SaveAsPrefab(GameObject obj, string name)
    {
        var warnings = new List<string>();
        bool ok = Prefabs.Save(obj, name, warnings, out var message);
        Say(message, error: !ok);
        foreach (var warning in warnings) Say("Warning: " + warning, error: true);
        return ok;
    }

    private void ApplyToPrefab(GameObject obj, string name)
    {
        if (!SaveAsPrefab(obj, name)) return;
        int updated = PrefabLibrary.RebuildInstances(Scene, name, except: obj);
        if (updated > 0) Say($"Updated {updated} other cop{(updated == 1 ? "y" : "ies")} of '{name}'.");
    }

    private void OpenScriptSource(Type type)
    {
        var file = Scripts.FindSourceFile(type.Name);
        if (file == null) Say($"Could not find the source file of {type.Name}.", error: true);
        else OpenInEditor(file);
    }
}
