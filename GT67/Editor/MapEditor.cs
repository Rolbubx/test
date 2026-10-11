using GT67.Core;
using GT67.Gui;
using GT67.UI;
using GT67.Utils;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;

namespace GT67.Editor;

public enum EditorTool
{
    /// <summary>Click objects to select them.</summary>
    Select,

    /// <summary>Select, and drag the arrows to move the selected object.</summary>
    Move,

    /// <summary>Select, and drag the rings to rotate the selected object.</summary>
    Rotate,

    /// <summary>Select, and drag the cubes to scale the selected object.</summary>
    Scale,

    /// <summary>Sculpt and paint the terrain with a brush.</summary>
    Terrain,
}

/// <summary>
/// The in-game map editor (enabled with <c>IsEditor</c> in Program.cs): docked panels around a 3D viewport. Drop model
/// files onto the window, drag shapes, prefabs and scripts out of the Project panel, move/rotate/scale objects and lights with gizmos, edit
/// the lighting and post-processing, design the game's UI, attach C# scripts (edited in Visual Studio Code or any other
/// editor), sculpt terrain, press Play to try it and Compile to write the whole game to <c>Generated/GeneratedGame/</c>.
/// </summary>
public sealed partial class MapEditor : IDisposable
{
    private readonly Window _window;
    private readonly TransformGizmo _gizmo;
    private readonly LightMarkers _markers;
    private readonly BrushRing _ring;
    private readonly List<(string text, bool error)> _output = new();
    private readonly Dictionary<GameObject, (Vector3 position, Quaternion rotation, Vector3 scale, bool enabled, bool visible)> _snapshot = new();
    private readonly HashSet<GameObject> _existedAtPlay = new();

    private bool _scrollOutput;
    private bool _recompilePending;

    // Pressing on an object starts a pending drag that becomes real once the mouse moves a few pixels.
    private bool _freeDragPending;
    private Ray _pressRay;
    private Vector2 _pressMouse;

    // A rectangle drawn around the selected water (a unit square, scaled to the water's size).
    private readonly GameObject _waterOutline;

    private static GameObject CreateWaterOutline()
    {
        var corners = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) };
        var vertices = corners.Select(c => new Vertex(c, Vector3.UnitY, Vector2.Zero)).ToArray();
        var mesh = new Mesh(vertices, new uint[] { 0, 1, 1, 2, 2, 3, 3, 0 }, OpenTK.Graphics.OpenGL4.PrimitiveType.Lines);
        return new GameObject(mesh, Vector3.Zero)
        {
            Name = "Water outline",
            Color = new Color4(0.35f, 0.85f, 1f, 1f),
            Unlit = true,
            CastShadow = false,
            ReceiveShadow = false,
            Collision = CollisionMode.None,
            EditorOnly = true,
            Overlay = true,
            Visible = false,
        };
    }

    // The editor's own view, put back when Play stops (the game camera moves the camera while playing).
    private (Vector3 position, float yaw, float pitch, float fov) _editorView;

    public MapEditor(Window window)
    {
        _window = window;
        Paths = ProjectPaths.Locate();
        // Shaders written in the editor live in the project (Shaders/Custom) and are found before the copies in the build output
        if (Paths != null) window.Renderer.Custom.AddDirectory(Path.Combine(Paths.Root, "Shaders", "Custom"), first: true);
        Settings = EditorSettings.Load(Paths);
        Scripts = new ScriptWorkspace(Paths);
        Prefabs = new PrefabLibrary(Paths, Scripts);

        _gizmo = new TransformGizmo(Scene);
        _waterOutline = CreateWaterOutline();
        Scene.Add(_waterOutline);
        _markers = new LightMarkers(Scene);
        _ring = new BrushRing(Scene);
        Scene.Simulate = false; // edit first, press Play to run scripts
        InitScenes();
        ApplySnapSettings();

        if (Paths == null)
            Say("Project folder (GT67.csproj) not found: scripts and the map cannot be saved.", error: true);
        else
            CompileScripts(announceSuccess: false);

        Say("Editor ready. Drop .fbx/.gltf/.glb/.obj files onto the window, or drag shapes from the Project panel (bottom). The Hierarchy lists everything in the scene.");
    }

    public ProjectPaths? Paths { get; }
    public ScriptWorkspace Scripts { get; }
    public PrefabLibrary Prefabs { get; }
    public EditorSettings Settings { get; }
    public EditorTool Tool { get; set; } = EditorTool.Select;
    public GizmoSpace Space { get; set; } = GizmoSpace.World;
    public bool Playing { get; private set; }

    private Scene Scene => _window.Scene;
    private Gui.Gui G => _window.Ui.Gui;

    // ---- selection: one thing at a time, whatever its kind (object, light, water, sound, Volume, environment) ----

    private GameObject? Selected
    {
        get => _window.DebugUI.Selected;
        set
        {
            if (value != null) ClearSelection();
            _window.DebugUI.Selected = value;
        }
    }

    /// <summary>The point or spot light picked in the viewport (shown in the inspector, movable with the gizmo).</summary>
    private Light? SelectedLight { get; set; }

    /// <summary>The scene's Volume (post-processing settings) is shown in the inspector.</summary>
    private bool VolumeSelected { get; set; }

    /// <summary>The scene's environment (sun, sky, clouds, rain, fog, ambient light) is shown in the inspector.</summary>
    private bool EnvironmentSelected { get; set; }

    /// <summary>The body of water picked in the viewport or the Hierarchy (Inspector, move and scale gizmos).</summary>
    private Water? SelectedWater { get; set; }

    /// <summary>The sound picked in the Hierarchy.</summary>
    private Sound? SelectedSound { get; set; }

    private void ClearSelection()
    {
        _window.DebugUI.Selected = null;
        SelectedLight = null;
        VolumeSelected = false;
        EnvironmentSelected = false;
        SelectedWater = null;
        SelectedSound = null;
        SelectedUi = null;
    }

    private void SelectVolume()
    {
        ClearSelection();
        VolumeSelected = true;
        _rightTab = RightInspector;
    }

    private void SelectEnvironment()
    {
        ClearSelection();
        EnvironmentSelected = true;
        _rightTab = RightInspector;
    }

    private void SelectSound(Sound sound)
    {
        ClearSelection();
        SelectedSound = sound;
        _rightTab = RightInspector;
    }

    private void SelectLight(Light? light)
    {
        if (light != null) ClearSelection();
        SelectedLight = light;
    }

    private void SelectWater(Water? water)
    {
        if (water != null)
        {
            ClearSelection();
            _rightTab = RightInspector;
        }
        SelectedWater = water;
    }

    /// <summary>The closest water surface the ray crosses inside its rectangle, and how far along the ray.</summary>
    private Water? PickWater(Ray ray, out float distance)
    {
        Water? best = null;
        distance = float.MaxValue;
        if (MathF.Abs(ray.Direction.Y) < 1e-5f) return null;
        foreach (var water in Scene.Waters)
        {
            float t = (water.Level - ray.Origin.Y) / ray.Direction.Y;
            if (t <= 0f || t >= distance) continue;
            var p = ray.GetPoint(t);
            if (water.ReachHorizon ||
                (MathF.Abs(p.X - water.Position.X) <= water.Width * 0.5f && MathF.Abs(p.Z - water.Position.Z) <= water.Depth * 0.5f))
            {
                best = water;
                distance = t;
            }
        }
        return best;
    }

    private void ApplySnapSettings()
    {
        _gizmo.SnapEnabled = Settings.SnapEnabled;
        _gizmo.MoveSnap = Settings.MoveSnap;
        _gizmo.RotateSnap = Settings.RotateSnap;
        _gizmo.ScaleSnap = Settings.ScaleSnap;
    }

    private GizmoMode? ActiveGizmoMode => Tool switch
    {
        EditorTool.Move => GizmoMode.Move,
        EditorTool.Rotate => GizmoMode.Rotate,
        EditorTool.Scale => GizmoMode.Scale,
        _ => null,
    };

    private GizmoTarget? CurrentTarget()
    {
        if (Playing || UiMode) return null;
        if (SelectedWater != null) return GizmoTarget.For(SelectedWater);
        return SelectedLight switch
        {
            PointLight p => GizmoTarget.For(p),
            SpotLight s => GizmoTarget.For(s),
            _ => Selected is { IsDestroyed: false } obj ? GizmoTarget.For(obj) : null,
        };
    }

    // ---- messages --------------------------------------------------------------------------------

    private void Say(string text, bool error = false)
    {
        _output.Add((text, error));
        if (_output.Count > 200) _output.RemoveAt(0);
        _scrollOutput = true;
        if (error) Log.Warn(text); else Log.Info(text);
    }

    // ---- input -----------------------------------------------------------------------------------

    /// <summary>Left click in the viewport (not over a panel).</summary>
    public void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.Button != MouseButton.Left || Playing) return;

        if (UiMode)
        {
            BeginUiDrag();
            return;
        }
        if (Tool == EditorTool.Terrain) return; // strokes are handled in Update

        var ray = _window.GetMouseRay();
        var target = CurrentTarget();
        if (ActiveGizmoMode != null && target != null && _gizmo.TryBeginDrag(ray, target)) return;

        if (_markers.Pick(ray, _window.Camera) is { } light)
        {
            SelectLight(light);
            return;
        }

        var hit = Scene.Raycast(ray.Origin, ray.Direction, out float hitDistance);
        if (PickWater(ray, out float waterDistance) is { } water && (hit == null || waterDistance < hitDistance))
        {
            SelectWater(water);
            if (Tool is EditorTool.Select or EditorTool.Move)
            {
                _pressRay = ray;
                _pressMouse = _window.MousePosition;
                _freeDragPending = true;
            }
            return;
        }

        if (hit != null)
        {
            Selected = hit;
            // Press on an object and drag to slide it around (Select and Move tools); a plain click only selects it.
            if (Tool is EditorTool.Select or EditorTool.Move && !Scene.Terrains.Any(t => t.Object == hit))
            {
                _pressRay = ray;
                _pressMouse = _window.MousePosition;
                _freeDragPending = true;
            }
        }
        else
        {
            Selected = null;
            SelectLight(null);
            SelectWater(null);
        }
    }

    public void OnKeyDown(Keys key)
    {
        var keyboard = _window.KeyboardState;
        bool ctrl = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);

        switch (key)
        {
            case Keys.Delete:
                DeleteSelection();
                return;
            case Keys.Escape:
                ClearSelection();
                return;
            case Keys.D when ctrl:
                DuplicateSelection();
                return;
            case Keys.F:
                FocusSelection();
                return;
        }

        if (_window.IsLooking || ctrl) return; // WASD belongs to the fly camera
        switch (key)
        {
            case Keys.Q: Tool = EditorTool.Select; break;
            case Keys.W: Tool = EditorTool.Move; break;
            case Keys.E: Tool = EditorTool.Rotate; break;
            case Keys.R: Tool = EditorTool.Scale; break;
            case Keys.T: Tool = EditorTool.Terrain; break;
        }
    }

    private void DeleteSelection()
    {
        if (UiMode && SelectedUi != null)
        {
            Say($"Deleted UI element '{SelectedUi.Name}'.");
            _window.GameUi.Remove(SelectedUi);
            SelectedUi = null;
        }
        else if (SelectedWater is { } water)
        {
            Scene.Remove(water);
            water.Dispose();
            Say($"Deleted '{water.Name}'.");
            SelectWater(null);
        }
        else if (SelectedSound is { } sound)
        {
            Scene.Remove(sound);
            Say($"Deleted '{sound.Name}'.");
            SelectedSound = null;
        }
        else if (SelectedLight is PointLight point)
        {
            Scene.PointLights.Remove(point);
            Say($"Deleted '{point.Name}'.");
            SelectLight(null);
        }
        else if (Selected is { EditorOnly: false } obj && !Scene.Terrains.Any(t => t.Object == obj))
        {
            Say($"Deleted '{obj.Name}'.");
            obj.Destroy();
            Selected = null;
        }
    }

    private void DuplicateSelection()
    {
        if (Selected is not { EditorOnly: false } obj || Scene.Terrains.Any(t => t.Object == obj)) return;

        var clone = obj.Clone();
        clone.Translate(1f, 0f, 0f);
        if (obj.Parent != null) obj.Parent.AddChild(clone);
        else Scene.Add(clone);
        Selected = clone;
        Say($"Duplicated '{obj.Name}'.");
    }

    /// <summary>Moves the camera so the selected object or light fills the view.</summary>
    private void FocusSelection()
    {
        Vector3 center;
        float radius;
        if (SelectedWater != null)
        {
            center = SelectedWater.Position;
            radius = MathF.Max(SelectedWater.Width, SelectedWater.Depth) * 0.5f;
        }
        else if (SelectedLight != null)
        {
            center = LightMarkers.PositionOf(SelectedLight);
            radius = 1f;
        }
        else if (Selected is { } obj)
        {
            center = obj.Bounds.Center;
            radius = MathF.Max(obj.BoundingSphere.Radius, 0.5f);
        }
        else return;

        var camera = _window.Camera;
        camera.Position = center - camera.Front * MathF.Max(radius * 2.8f, 3f);
    }

    /// <summary>Model files dropped onto the window from the file manager.</summary>
    public void OnFileDrop(string[] files)
    {
        var at = _window.GetMouseWorldPosition();
        int offset = 0;
        foreach (var file in files)
        {
            if (AudioClip.IsAudioFile(file))
            {
                ImportSound(file, at);
                continue;
            }

            if (!ModelImporter.IsModelFile(file))
            {
                Say($"'{Path.GetFileName(file)}' is not a model or sound file (fbx, gltf, glb, obj, dae... wav, ogg, mp3).", error: true);
                continue;
            }

            var obj = ModelImporter.Import(Scene, file, at + new Vector3(offset * 2f, 0f, 0f), out var message, Paths);
            Say(message, error: obj == null);
            if (obj != null) Selected = obj;
            offset++;
        }
    }

    /// <summary>Copies a dropped sound file into Assets/Sounds and places a <see cref="Sound"/> for it in the scene.</summary>
    private void ImportSound(string file, Vector3 at)
    {
        string fileName = Path.GetFileName(file);
        string relative = "Assets/Sounds/" + fileName;
        try
        {
            // Into the project (kept) and the build output (so it plays right now, before the next build)
            foreach (var root in new[] { Paths?.Root, AppContext.BaseDirectory })
            {
                if (root == null) continue;
                string target = Path.Combine(root, relative);
                if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Say($"Could not copy '{fileName}': {e.Message}", error: true);
            return;
        }

        string baseName = Path.GetFileNameWithoutExtension(fileName), name = baseName;
        for (int i = 2; Scene.FindSound(name) != null; i++) name = $"{baseName} {i}";
        var sound = new Sound(relative) { Name = name, Position = at };
        Scene.Add(sound);
        SelectSound(sound);
        Say($"Added sound '{name}' ({sound.Length:0.0} s): it is in the Hierarchy, and Play in the Inspector previews it.");
    }

    // ---- per-frame -------------------------------------------------------------------------------

    public void Update(float deltaTime)
    {
        bool captured = _window.CursorState == CursorState.Grabbed;
        bool leftDown = _window.MouseState.IsButtonDown(MouseButton.Left);
        bool overUi = G.IsPointOverUi(_window.MousePosition) || !_window.Viewport.Contains(_window.MousePosition);

        // The selected water: forget it if it left the scene, outline it otherwise.
        if (SelectedWater != null && !Scene.Waters.Contains(SelectedWater)) SelectedWater = null;
        _waterOutline.Visible = SelectedWater is { ReachHorizon: false } && !Playing;
        if (SelectedWater is { } water)
        {
            _waterOutline.Position = water.Position + new Vector3(0f, 0.02f, 0f);
            _waterOutline.Scale = new Vector3(water.Width, 1f, water.Depth);
        }

        // Gizmo (its handles only show for the move/rotate/scale tools, but the object can be dragged with any of them)
        var mode = ActiveGizmoMode;
        var target = CurrentTarget();
        _gizmo.Update(_window.Camera, mode != null ? target : null, mode ?? GizmoMode.Move, Space);

        if (_freeDragPending)
        {
            if (!leftDown || target == null || captured) _freeDragPending = false;
            else if ((_window.MousePosition - _pressMouse).LengthSquared > 16f) // 4 px: a click is not a drag
            {
                _freeDragPending = false;
                var keyboard = _window.KeyboardState;
                bool vertical = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
                _gizmo.BeginFreeDrag(_pressRay, target, vertical);
            }
        }

        if (target != null && !captured && (mode != null || _gizmo.IsDragging))
        {
            var ray = _window.GetMouseRay();
            if (_gizmo.IsDragging)
            {
                if (leftDown) _gizmo.Drag(ray, target); else _gizmo.EndDrag();
            }
            else if (!overUi)
            {
                _gizmo.Hover(ray, target);
            }
        }
        else if (_gizmo.IsDragging)
        {
            _gizmo.EndDrag();
        }
        _markers.Update(_window.Camera, !Playing, SelectedLight);

        UpdateUiDrag(leftDown);

        // Terrain brush
        if (Tool == EditorTool.Terrain && !Playing && !captured) HandleTerrain(deltaTime, overUi, leftDown);
        else
        {
            _ring.Hide();
            if (_stroking) EndStroke();
        }
    }

    // ---- play / stop -----------------------------------------------------------------------------

    public void Play()
    {
        if (Playing) return;
        _snapshot.Clear();
        _existedAtPlay.Clear();
        _playedScenes.Clear();
        _playStartScene = Scene;
        Scene.Volume.CaptureFrom(_window.Renderer.Post);   // Stop puts these post-processing settings back
        SnapshotScene(Scene);
        _window.Scenes.LoadOverride = LoadDuringPlay;
        SnapshotUi();
        var camera = _window.Camera;
        _editorView = (camera.Position, camera.Yaw, camera.Pitch, camera.Fov);
        _window.Game.ViewDetached = false;
        Playing = true;
        Scene.Simulate = true;
        Say("Playing: scripts run and the game UI works. Press Stop to restore the scene." +
            (FindCameraObject() == null ? "" : " 'Free view' in the toolbar lets you fly around meanwhile."));
    }

    public void Stop()
    {
        if (!Playing) return;
        Playing = false;
        _window.Scenes.LoadOverride = null;

        // Back to the scene Play started in (scripts may have loaded others), with its own post-processing.
        if (_playStartScene != null && _playStartScene != Scene) OpenScene(_playStartScene, keepVolume: false);
        else _window.Scenes.Activate(Scene);
        _playStartScene = null;

        // Remove what the scripts spawned, put everything else back and give scripts a fresh start.
        foreach (var scene in _playedScenes)
        {
            scene.Simulate = false;
            foreach (var sound in scene.Sounds) sound.Stop();
            foreach (var obj in scene.Traverse().ToList())
                if (!obj.EditorOnly && !_existedAtPlay.Contains(obj)) obj.Destroy();
        }
        _playedScenes.Clear();

        foreach (var (obj, state) in _snapshot)
        {
            if (obj.IsDestroyed) continue;
            obj.Position = state.position;
            obj.Rotation = state.rotation;
            obj.Scale = state.scale;
            obj.Enabled = state.enabled;
            obj.Visible = state.visible;

            foreach (var script in obj.Scripts.ToList())
                ScriptWorkspace.ReplaceScript(obj, script, script.GetType());
        }
        _snapshot.Clear();
        RestoreUi();
        _window.GameUi.ShowCursor = false;
        _window.Game.Reset();   // timers, time scale, script lights, camera mode
        _window.Game.ViewDetached = false;

        var camera = _window.Camera;
        (camera.Position, camera.Yaw, camera.Pitch, camera.Fov) = _editorView;
        Say("Stopped: scene restored.");
    }

    // ---- compile ---------------------------------------------------------------------------------

    /// <summary>Compiles the scripts, swaps the new versions onto their objects, and writes the generated map.</summary>
    public void Compile()
    {
        if (Playing) { Say("Stop playing before compiling.", error: true); return; }
        if (Paths == null) { Say("Project folder (GT67.csproj) not found.", error: true); return; }

        if (!CompileScripts(announceSuccess: true))
        {
            Say("Map not written: fix the script errors first.", error: true);
            return;
        }

        try
        {
            Scene.Volume.CaptureFrom(_window.Renderer.Post);   // the open scene's post-processing as it is now
            var result = new Export.GameExporter(_scenes, Paths, _window.GameUi).Generate();
            Say($"Wrote {result.Files.Count} files to {Path.GetRelativePath(Paths.Root, result.Folder)}: " +
                $"{result.Scenes} scene(s) (the game starts with '{_scenes[0].Name}'), " +
                $"{result.Objects} object(s), {result.Terrains} terrain(s), {result.Waters} water body(ies), " +
                $"{result.Sounds} sound(s), {result.UiElements} UI element(s); {result.CopiedFiles} asset file(s) shipped " +
                "(models, textures, sounds, UI images).");
            foreach (var warning in result.Warnings) Say("Warning: " + warning, error: true);
            Say("Rebuild and start with --generated (dotnet run -- --generated) to play the generated game.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Say($"Could not write the map: {e.Message}", error: true);
        }
    }

    private bool CompileScripts(bool announceSuccess)
    {
        var result = Scripts.Compile();
        foreach (var error in result.Errors) Say(error, error: true);
        if (!result.Success) return false;

        int swapped = Scripts.ReattachScripts(Scene);
        if (announceSuccess || swapped > 0)
            Say($"Scripts compiled in {result.Duration.TotalMilliseconds:0} ms: {Scripts.Types.Count} script type(s)" +
                (swapped > 0 ? $", {swapped} attached script(s) updated." : "."));
        return true;
    }

    /// <summary>Recompiles when a script file was saved in the external editor (and the game is not running).</summary>
    private void WatchScripts()
    {
        if (Settings.AutoCompile && Scripts.ConsumeChange(TimeSpan.FromMilliseconds(450))) _recompilePending = true;
        if (!_recompilePending || Playing) return;

        _recompilePending = false;
        Say("A script file changed: recompiling...");
        CompileScripts(announceSuccess: true);
    }

    // ---- spawning --------------------------------------------------------------------------------

    private GameObject? Spawn(PaletteItem item, Vector3 position)
    {
        if (item.Kind == "model")
        {
            var imported = ModelImporter.Import(Scene, item.Path!, position, out var message);
            Say(message, error: imported == null);
            if (imported != null) Selected = imported;
            return imported;
        }

        if (item.Kind == "water")
        {
            AddWaterAt(position);
            return null;
        }

        if (item.Kind == "prefab")
        {
            var instance = Prefab.Instantiate(item.Path!, position);
            if (instance == null)
            {
                Say($"Prefab '{item.Path}' could not be loaded (see the log).", error: true);
                return null;
            }
            // Stand it on the point, like the shapes.
            instance.Translate(0f, position.Y - instance.Bounds.Min.Y, 0f);
            instance.Name = ModelImporter.UniqueName(Scene, instance.Name);
            Scene.Add(instance);
            Selected = instance;
            Say($"Added '{instance.Name}' (prefab {item.Path}).");
            return instance;
        }

        var mesh = item.Make!();
        var obj = new GameObject(mesh, position + new Vector3(0f, -mesh.Bounds.Min.Y, 0f))
        {
            Name = ModelImporter.UniqueName(Scene, item.Name),
        };
        Scene.Add(obj);
        Selected = obj;
        Say($"Added {obj.Name}.");
        return obj;
    }

    private void AddPointLight()
    {
        var position = _window.Camera.Position + _window.Camera.Front * 5f + Vector3.UnitY;
        var light = new PointLight(position, Color4.White, 3f, 12f) { Name = "Point Light" };
        Scene.AddLight(light);
        if (Scene.PointLights.Contains(light)) SelectLight(light);
        Say("Added a point light: drag it in the viewport or tune it in the Inspector.");
    }

    /// <summary>A capsule tagged "Player" that walks with PlayerController; adds a game camera following it if there is none.</summary>
    private void AddPlayer()
    {
        var controller = FindScriptType("PlayerController");
        if (controller == null) return;

        const float radius = 0.35f, height = 1.8f;
        var feet = GroundPointInFront(6f);
        var player = new GameObject(Utils.Primitives.Capsule(radius, height, 24), feet + new Vector3(0f, height * 0.5f, 0f))
        {
            Name = ModelImporter.UniqueName(Scene, "Player"),
            Tag = "Player",
            Color = new Color4(0.25f, 0.55f, 1f, 1f),
        };
        var script = player.AddScript(controller);
        // One collision sphere filling the bottom of the capsule, so the feet stand on the ground.
        ParameterAccess.TrySet(script, "Radius", radius);
        ParameterAccess.TrySet(script, "SphereOffset", radius - height * 0.5f);
        Scene.Add(player);

        bool addedCamera = false;
        if (FindCameraObject() == null && FindScriptType("GameCamera", quiet: true) != null)
        {
            var behind = player.Position + new Vector3(0f, 2.5f, 6f);
            addedCamera = AddCameraAt(behind, player.Position, fixedView: false) != null;
        }

        Selected = player;
        Say($"Added '{player.Name}': press Play and walk with WASD or the arrows, jump with Space." +
            (addedCamera ? " A game camera follows it (third person; change Mode in its Inspector)." : ""));
    }

    /// <summary>The game camera, placed where the editor camera is and looking the same way.</summary>
    private void AddCamera()
    {
        var view = _window.Camera;
        bool hasPlayer = Scene.Traverse().Any(o => string.Equals(o.Tag, "Player", StringComparison.OrdinalIgnoreCase));
        var camera = AddCameraAt(view.Position, view.Position + view.Front, fixedView: !hasPlayer);
        if (camera == null) return;

        Selected = camera;
        Say(hasPlayer
            ? $"Added '{camera.Name}': it follows the object tagged 'Player' when you press Play (Mode in the Inspector)."
            : $"Added '{camera.Name}' (Fixed: Play looks from here). Tag an object 'Player' and set Mode to follow it.");
        if (Scene.Traverse().Count(o => o.Scripts.Any(s => s.GetType().Name == "GameCamera")) > 1)
            Say("There is more than one game camera: the first enabled one is used.", error: true);
    }

    private GameObject? AddCameraAt(Vector3 position, Vector3 lookAt, bool fixedView)
    {
        var type = FindScriptType("GameCamera");
        if (type == null) return null;

        var camera = new GameObject(Utils.Primitives.CameraBody(), position)
        {
            Name = ModelImporter.UniqueName(Scene, "Camera"),
            Color = new Color4(0.2f, 0.22f, 0.26f, 1f),
            Collision = CollisionMode.None,   // the player walks through it
            CastShadow = false,
        };
        camera.LookAt(lookAt);
        var script = camera.AddScript(type);
        if (fixedView) ParameterAccess.TrySet(script, "Mode", "Fixed");
        Scene.Add(camera);
        return camera;
    }

    /// <summary>The first object with a GameCamera script, or null.</summary>
    private GameObject? FindCameraObject() =>
        Scene.Traverse().FirstOrDefault(o => !o.EditorOnly && o.Scripts.Any(s => s.GetType().Name == "GameCamera"));

    /// <summary>A compiled script class from Scripts/, by name (with a message when it is missing).</summary>
    private Type? FindScriptType(string name, bool quiet = false)
    {
        var type = Scripts.Types.FirstOrDefault(t => t.Name == name);
        if (type == null && !quiet)
            Say($"Script '{name}' not found: Scripts/{name}.cs is missing or the scripts do not compile.", error: true);
        return type;
    }

    /// <summary>The ground below the point <paramref name="distance"/> in front of the editor camera (y = 0 if there is none).</summary>
    private Vector3 GroundPointInFront(float distance)
    {
        var camera = _window.Camera;
        var flat = new Vector3(camera.Front.X, 0f, camera.Front.Z);
        flat = flat.LengthSquared > 1e-4f ? Vector3.Normalize(flat) : -Vector3.UnitZ;
        var point = camera.Position + flat * distance;
        var hit = Scene.Raycast(new Vector3(point.X, point.Y + 200f, point.Z), -Vector3.UnitY, out float d);
        point.Y = hit != null ? point.Y + 200f - d : 0f;
        return point;
    }

    /// <summary>Adds a lake on the ground in front of the camera (the button; dragging it into the viewport uses <see cref="AddWaterAt"/>).</summary>
    private void AddWater() => AddWaterAt(GroundPointInFront(14f));

    /// <summary>
    /// Adds a 16 x 16 m lake centred on <paramref name="ground"/>. On a terrain it digs its own basin and fills it just
    /// below the ground; elsewhere it lies a little above the ground (lower the ground or move it). It is selected, so
    /// the Inspector shows it and the Move / Scale tools (W / R) move and resize it.
    /// </summary>
    private void AddWaterAt(Vector3 ground)
    {
        const float size = 16f;
        string name = "Water";
        for (int i = 2; Scene.Waters.Any(w => w.Name == name); i++) name = $"Water {i}";

        var terrain = TerrainUnder(ground);
        Water water;
        if (terrain != null)
        {
            float level = ground.Y - 0.15f;
            terrain.CarveBasin(ground, size * 0.5f, size * 0.5f, level - 2.5f);
            water = new Water(ground.X, level, ground.Z, size, size) { Name = name };
            Say($"Added '{name}' and dug a basin for it in the terrain. Resize it with the Scale tool (R) or in the Inspector.");
        }
        else
        {
            water = new Water(ground.X, ground.Y + 0.3f, ground.Z, size, size) { Name = name };
            Say($"Added '{name}'. There is no terrain under it, so it sits on the ground: lower the ground there, " +
                "or set its Level, Width and Depth in the Inspector.");
        }
        Scene.Add(water);
        SelectWater(water);
        if (Tool is EditorTool.Select or EditorTool.Terrain) Tool = EditorTool.Move;
    }

    /// <summary>Digs a basin under the selected water (its size, 2.5 m deep), if it lies on a terrain.</summary>
    private void DigBasin(Water water)
    {
        var terrain = TerrainUnder(water.Position);
        if (terrain == null)
        {
            Say("There is no terrain under this water to dig into.", error: true);
            return;
        }
        terrain.CarveBasin(water.Position, water.Width * 0.5f, water.Depth * 0.5f, water.Level - 2.5f);
        Say($"Dug a basin under '{water.Name}'.");
    }

    private Terrain? TerrainUnder(Vector3 point) => Scene.Terrains.FirstOrDefault(t =>
        MathF.Abs(point.X - t.Object.Position.X) <= t.Width * 0.5f && MathF.Abs(point.Z - t.Object.Position.Z) <= t.Depth * 0.5f);

    /// <summary>Lowers (or lifts) the object until its bottom touches the first surface below it.</summary>
    private void DropToGround(GameObject obj)
    {
        bool wasVisible = obj.Visible;
        obj.Visible = false; // so the ray does not hit the object itself
        var bounds = obj.Bounds;
        var from = new Vector3(bounds.Center.X, bounds.Max.Y + 50f, bounds.Center.Z);
        var hit = Scene.Raycast(from, -Vector3.UnitY, out float distance);
        obj.Visible = wasVisible;

        if (hit == null) { Say("Nothing below to land on.", error: true); return; }
        float groundY = from.Y - distance;
        obj.Translate(0f, groundY - bounds.Min.Y, 0f);
    }

    public void Dispose()
    {
        Settings.Save(Paths);
        Scripts.Dispose();
        // The open scene is disposed by the window, the others here. Gizmo, markers and ring are objects of the open scene.
        foreach (var scene in _scenes)
            if (scene != Scene) scene.Dispose();
    }
}
