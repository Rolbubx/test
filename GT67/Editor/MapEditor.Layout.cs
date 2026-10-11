using GT67.Core;
using GT67.Gui;
using GT67.UI;
using OpenTK.Mathematics;
using SkiaSharp;

namespace GT67.Editor;

// The editor's layout, like Unity: the toolbar on top, the Hierarchy (every item of the open scene) on the left, the
// Inspector (whatever is selected) on the right, the Project browser and the Console along the bottom, and the 3D view
// in the middle.
public sealed partial class MapEditor
{
    private const float ToolbarHeight = 52f;
    private const float SplitterThickness = 6f;

    private static readonly string[] LeftTabs = { "Hierarchy", "UI" };
    private static readonly IconKind[] LeftIcons = { IconKind.Scene, IconKind.Layout };
    private static readonly string[] RightTabs = { "Inspector", "Render" };
    private static readonly IconKind[] RightIcons = { IconKind.Select, IconKind.Camera };

    private const int LeftUi = 1;
    private const int RightInspector = 0;

    private float _leftWidth = 280f, _rightWidth = 360f, _bottomHeight = 240f;
    private int _leftTab, _rightTab;
    private EditorTool _lastTool = EditorTool.Select;
    private string _hierarchyFilter = "";

    /// <summary>True while the UI designer is in front: the viewport edits the game UI instead of the 3D scene.</summary>
    private bool UiMode => _leftTab == LeftUi;

    // ---- the whole layout ------------------------------------------------------------------------

    public void Draw(float deltaTime)
    {
        _window.DebugUI.Draw(deltaTime); // keeps the FPS smoothing running; the debug window itself stays hidden here
        WatchScripts();

        var g = G;
        var size = g.DisplaySize;
        _leftWidth = Math.Clamp(_leftWidth, 220f, MathF.Max(220f, size.X * 0.4f));
        _rightWidth = Math.Clamp(_rightWidth, 260f, MathF.Max(260f, size.X * 0.45f));
        _bottomHeight = Math.Clamp(_bottomHeight, 120f, MathF.Max(120f, size.Y * 0.6f));

        // Switching to the terrain brush shows its settings in the Inspector.
        if (Tool != _lastTool)
        {
            if (Tool == EditorTool.Terrain) _rightTab = RightInspector;
            _lastTool = Tool;
        }

        // Unity's arrangement: the Inspector runs the full height on the right, the bottom panel spans everything left of it.
        float bodyTop = ToolbarHeight;
        float bodyHeight = size.Y - bodyTop;
        var right = new UiRect(size.X - _rightWidth, bodyTop, _rightWidth, bodyHeight);
        var bottom = new UiRect(0f, size.Y - _bottomHeight, right.X, _bottomHeight);
        var left = new UiRect(0f, bodyTop, _leftWidth, bottom.Y - bodyTop);
        _window.Viewport = new UiRect(left.Right, bodyTop, right.X - left.Right, left.Height);

        const WindowFlags dock = WindowFlags.Fixed | WindowFlags.Panel;
        if (g.Begin("Toolbar", new UiRect(0f, 0f, size.X, ToolbarHeight), dock | WindowFlags.NoScroll)) DrawToolbar();
        g.End();

        if (g.Begin("Left", left, dock))
        {
            _leftTab = g.TabBar("lefttabs", LeftTabs, _leftTab, LeftIcons, compact: true);
            g.Spacing(4f);
            if (UiMode) DrawUiTree();
            else DrawHierarchy();
        }
        g.End();

        if (g.Begin("Right", right, dock))
        {
            _rightTab = g.TabBar("righttabs", RightTabs, _rightTab, RightIcons, compact: true);
            g.Spacing(4f);
            if (_rightTab == RightInspector) DrawInspectorPanel();
            else _window.DebugUI.DrawRender();
        }
        g.End();

        DrawBottomPanel(bottom, dock);

        g.Splitter("left", new UiRect(left.Right - SplitterThickness * 0.5f, bodyTop, SplitterThickness, left.Height), true, ref _leftWidth, 220f, size.X * 0.4f, 1f);
        g.Splitter("right", new UiRect(right.X - SplitterThickness * 0.5f, bodyTop, SplitterThickness, bodyHeight), true, ref _rightWidth, 260f, size.X * 0.45f, -1f);
        g.Splitter("bottom", new UiRect(bottom.X, bottom.Y - SplitterThickness * 0.5f, bottom.Width, SplitterThickness), false, ref _bottomHeight, 120f, size.Y * 0.6f, -1f);

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
        g.Tooltip("Run the scripts and the game UI in the open scene. Stop restores every scene.");

        g.SameLine();
        if (Playing)
        {
            var game = _window.Game;
            if (g.IconTextButton(game.ViewDetached ? IconKind.Camera : IconKind.Eye, game.ViewDetached ? "Player view" : "Free view", 140f))
                game.ViewDetached = !game.ViewDetached;
            g.Tooltip(game.ViewDetached
                ? "Back to the game camera (what the player sees)."
                : "Detach the view from the game camera: fly around with the right mouse button + WASD while the game runs.");
        }
        else
        {
            if (g.IconTextButton(IconKind.Compile, "Compile", 120f)) Compile();
            g.Tooltip("Compile the scripts, then write the game (every scene) into Generated/GeneratedGame/,\nwith the models, textures and sounds it needs. Run it with: dotnet run -c Game");
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
        if (g.Button(Space == GizmoSpace.World ? "World" : "Local", 76f)) Space = Space == GizmoSpace.World ? GizmoSpace.Local : GizmoSpace.World;
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
        var statusRect = g.Reserve(0f, g.Theme.ButtonHeight);
        g.LabelRect(statusRect.Deflate(8f, 0f), $"{Scene.Name}  ·  {SelectionName()}", g.Theme.TextDim, g.Theme.FontSize, FontWeight.Regular, TextAlign.Left);
    }

    private string SelectionName() =>
        SelectedLight != null ? $"Light: {SelectedLight.Name}"
        : SelectedWater != null ? $"Water: {SelectedWater.Name}"
        : SelectedSound != null ? $"Sound: {SelectedSound.Name}"
        : Selected != null ? Selected.Name
        : SelectedUi != null ? $"UI: {SelectedUi.Name}"
        : VolumeSelected ? "Volume"
        : EnvironmentSelected ? "Environment"
        : "Nothing selected";

    private void ToolButton(IconKind icon, string tip, EditorTool tool)
    {
        var g = G;
        g.SameLine();
        if (g.IconButton(icon, tip, Tool == tool))
        {
            Tool = tool;
            if (UiMode) _leftTab = 0; // the 3D tools only make sense on the scene
            _rightTab = RightInspector;
        }
        g.Tooltip(tip);
    }

    private void DrawSettingsPopup()
    {
        var g = G;
        if (!g.BeginPopup("##settings", 340f)) return;

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
            string hint = Playing ? $"PLAYING  {Scene.Name}" : UiMode ? "UI designer: click to select, drag to move, corner to resize"
                : Tool == EditorTool.Terrain ? "Terrain brush: left-drag to sculpt" : "RMB + WASD fly   F focus   Del delete   Ctrl+D duplicate";
            var chip = new UiRect(vp.X + 12f, vp.Bottom - 38f, g.MeasureText(hint, 12f, FontWeight.Medium) + 24f, 26f);
            g.PanelRect(chip, Playing ? new SKColor(220, 70, 70, 230) : new SKColor(10, 12, 16, 170), 13f, SKColors.Transparent);
            g.LabelRect(chip, hint, SKColors.White, 12f, FontWeight.Medium, TextAlign.Center);
        }
        g.End();
    }

    // ---- hierarchy: everything in the open scene, in one list ------------------------------------

    private void DrawHierarchy()
    {
        var g = G;

        // The open scene (its menu switches, adds and renames scenes) and the Create menu.
        if (g.IconTextButton(IconKind.Scene, Scene.Name + "##scenebutton", MathF.Max(120f, g.RemainingWidth - g.Theme.ButtonHeight - 8f)))
            g.TogglePopup("##scenemenu", g.LastItemRect);
        g.Tooltip("The open scene. Click to open another one, add a scene or rename this one.");
        DrawSceneMenu();
        g.SameLine();
        if (g.IconButton(IconKind.Plus, "create")) g.TogglePopup("##create", g.LastItemRect);
        g.Tooltip("Create: shapes, lights, water, player, camera, sound...");
        DrawCreateMenu();

        g.InputTextBare("##hierarchyfilter", ref _hierarchyFilter, "Search the scene");
        g.Spacing(2f);

        if (Matches("Volume") && g.Selectable("Volume##volume", VolumeSelected, IconKind.Camera)) SelectVolume();
        g.Tooltip("This scene's post-processing: tone mapping, bloom, depth of field, colour grading... (each scene has its own)");
        if (Matches("Environment") && g.Selectable("Environment##environment", EnvironmentSelected, IconKind.Cloud)) SelectEnvironment();
        g.Tooltip("Sun and time of day, sky and clouds, rain, fog and ambient light.");

        foreach (var root in Scene.Roots.ToArray()) DrawSceneNode(root);

        for (int i = 0; i < Scene.PointLights.Count; i++)
        {
            var light = Scene.PointLights[i];
            if (Matches(light.Name) && g.Selectable($"{light.Name}##pl{i}", SelectedLight == light, IconKind.Light)) SelectLight(light);
            if (g.LastItemDoubleClicked && SelectedLight == light) FocusSelection();
        }
        if (Scene.Spot is { } spot && Matches("Spot light"))
        {
            if (g.Selectable("Spot light##spot", SelectedLight == spot, IconKind.Light)) SelectLight(spot);
            if (g.LastItemDoubleClicked && SelectedLight == spot) FocusSelection();
        }
        foreach (var water in Scene.Waters)
        {
            if (!Matches(water.Name)) continue;
            if (g.Selectable(water.Name + "##w" + water.GetHashCode(), water == SelectedWater, IconKind.Water)) SelectWater(water);
            if (g.LastItemDoubleClicked && water == SelectedWater) FocusSelection();
        }
        for (int i = 0; i < Scene.Sounds.Count; i++)
        {
            var sound = Scene.Sounds[i];
            if (Matches(sound.Name) && g.Selectable($"{sound.Name}##snd{i}", sound == SelectedSound, IconKind.Sound)) SelectSound(sound);
        }
    }

    private bool Matches(string name) =>
        _hierarchyFilter.Trim().Length == 0 || name.Contains(_hierarchyFilter.Trim(), StringComparison.OrdinalIgnoreCase);

    private void DrawSceneNode(GameObject obj)
    {
        if (obj.EditorOnly) return;
        var g = G;
        bool filtering = _hierarchyFilter.Trim().Length > 0;
        if (filtering)
        {
            // Searching: matching objects as a flat list.
            if (Matches(obj.Name))
            {
                if (g.Selectable($"{obj.Name}##{obj.Id}", obj == Selected, IconFor(obj))) Selected = obj;
                AcceptScriptDrop(obj);
                if (g.LastItemDoubleClicked && obj == Selected) FocusSelection();
            }
            foreach (var child in obj.Children.ToArray()) DrawSceneNode(child);
            return;
        }

        bool open = g.TreeNode($"{obj.Name}##{obj.Id}", obj == Selected, out bool clicked, obj.Children.Count == 0, IconFor(obj), defaultOpen: true);
        if (clicked) Selected = obj;
        AcceptScriptDrop(obj);
        if (g.LastItemDoubleClicked && obj == Selected) FocusSelection();
        if (!open) return;

        foreach (var child in obj.Children.ToArray()) DrawSceneNode(child);
        g.TreePop();
    }

    private IconKind IconFor(GameObject obj) =>
        Scene.Terrains.Any(t => t.Object == obj) ? IconKind.Terrain
        : obj.Scripts.Any(s => s.GetType().Name == "GameCamera") ? IconKind.Camera
        : obj.Children.Count > 0 ? IconKind.Folder
        : IconKind.Cube;

    /// <summary>A script dragged from the Project panel and released on this row is attached to the object.</summary>
    private void AcceptScriptDrop(GameObject obj)
    {
        var g = G;
        if (g.IsLastItemHovered && g.TryGetDrop(out var payload, out _) && payload is PaletteItem { Kind: "script" } script)
            AttachScript(obj, script);
    }

    /// <summary>Unity's "+" menu: everything that can be added to the scene.</summary>
    private void DrawCreateMenu()
    {
        var g = G;
        if (!g.BeginPopup("##create", 230f, 520f)) return;
        var front = _window.Camera.Position + _window.Camera.Front * 6f;

        g.SeparatorText("3D object");
        foreach (var item in Primitives)
            if (g.MenuItem(item.Name + "##create", icon: item.Name == "Sphere" ? IconKind.Sphere : IconKind.Cube)) Spawn(item, front);

        g.SeparatorText("World");
        if (g.MenuItem("Point light", icon: IconKind.Light)) AddPointLight();
        if (g.MenuItem("Water (lake)", icon: IconKind.Water)) AddWater();
        if (g.MenuItem("Terrain", "T", icon: IconKind.Terrain))
        {
            // One terrain per scene: create it if there is none, otherwise pick it up for sculpting.
            if (Scene.Terrains.FirstOrDefault() is { } existing)
            {
                Selected = existing.Object;
                Tool = EditorTool.Terrain;
            }
            else CreateTerrain();
        }

        g.SeparatorText("Gameplay");
        if (g.MenuItem("Player (capsule)", icon: IconKind.Sphere)) AddPlayer();
        if (g.MenuItem("Camera", icon: IconKind.Camera)) AddCamera();

        g.SeparatorText("Audio");
        if (g.MenuItem("Sound", icon: IconKind.Sound))
        {
            var sound = new Sound { Name = $"Sound {Scene.Sounds.Count + 1}", Position = front };
            Scene.Add(sound);
            SelectSound(sound);
            Say("Added an empty sound: set its file in the Inspector, or drag a sound from the Project panel into the scene.");
        }
        g.EndPopup();
    }

    // ---- inspector: whatever is selected ---------------------------------------------------------

    private void DrawInspectorPanel()
    {
        var g = G;
        if (UiMode)
        {
            DrawUiProperties();
            return;
        }
        if (Tool == EditorTool.Terrain && !Playing)
        {
            DrawTerrainPanel();
            return;
        }
        // A selected terrain shows its tools first, like Unity, then its object settings.
        if (Selected is { } picked && Scene.Terrains.Any(t => t.Object == picked) && !Playing)
        {
            DrawTerrainPanel();
            if (g.CollapsingHeader("Object (transform, material...)", defaultOpen: false)) _window.DebugUI.DrawInspector();
            return;
        }

        if (VolumeSelected)
        {
            g.Heading("Volume");
            g.TextWrapped($"Post-processing of the scene '{Scene.Name}'. Every scene has its own: opening another scene (or Game.LoadScene) switches to its settings.", dim: true);
            _window.DebugUI.DrawVolume();
        }
        else if (EnvironmentSelected)
        {
            g.Heading("Environment");
            g.TextWrapped($"Sun, sky, clouds, rain, fog and ambient light of the scene '{Scene.Name}'.", dim: true);
            _window.DebugUI.DrawEnvironment();
        }
        else if (SelectedLight != null) DrawLightInspector(SelectedLight);
        else if (SelectedWater != null) DrawWaterInspector(SelectedWater);
        else if (SelectedSound is { } sound)
        {
            if (!Scene.Sounds.Contains(sound))
            {
                SelectedSound = null;
                return;
            }
            g.Heading(sound.Name);
            g.TextDim("Sound");
            if (!_window.DebugUI.DrawSoundSettings(sound)) SelectedSound = null;
        }
        else _window.DebugUI.DrawInspector();
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
            g.TextDim("No scripts compiled yet (Project > Scripts, or the Console for errors).");
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
        g.Tooltip("Save this object, its children and scripts as Prefabs/<name>.prefab, to place copies from Project > Prefabs\nor spawn them from scripts with Game.SpawnPrefab(\"name\", position).");
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
