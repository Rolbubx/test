using GT67.Core;
using GT67.Gui;
using OpenTK.Mathematics;

namespace GT67.Editor;

// The scenes of the game, like Unity: the editor keeps every scene in memory and shows one at a time in the Hierarchy.
// Each has its own environment, objects and post-processing Volume. Compile writes them all; the game starts with the
// first one and scripts switch with Game.LoadScene("Name").
public sealed partial class MapEditor
{
    private readonly List<Scene> _scenes = new();

    // While playing: the scene Play started in, and every scene entered since (restored on Stop).
    private Scene? _playStartScene;
    private readonly HashSet<Scene> _playedScenes = new();

    // Naming a scene in the scene menu.
    private string _sceneName = "";
    private bool _renaming;

    /// <summary>The editor's scenes in game order (the first is where the game starts).</summary>
    public IReadOnlyList<Scene> Scenes => _scenes;

    /// <summary>Collects the scenes at start-up: every registered one (a compiled game opened with --generated) or the current one.</summary>
    private void InitScenes()
    {
        var manager = _window.Scenes;
        _ = manager.Defaults;   // the start-up post-processing, before any scene changes it
        foreach (var name in manager.Names)
        {
            if (string.Equals(name, Scene.Name, StringComparison.OrdinalIgnoreCase)) _scenes.Add(Scene);
            else if (manager.Build(name) is { } scene)
            {
                scene.Simulate = false;
                _scenes.Add(scene);
            }
        }
        if (!_scenes.Contains(Scene)) _scenes.Insert(0, Scene);
    }

    /// <summary>
    /// Shows another scene: keeps the post-processing edits of the one being left in its Volume, moves the editor's
    /// own helpers (gizmo, markers, outlines) across and applies the new scene's Volume.
    /// </summary>
    private void OpenScene(Scene target, bool keepVolume = true)
    {
        var current = Scene;
        if (target == current) return;
        if (keepVolume) current.Volume.CaptureFrom(_window.Renderer.Post);
        foreach (var sound in current.Sounds) sound.Stop();

        var helpers = current.Roots.Where(r => r.EditorOnly).ToArray();
        current.Remove(helpers);
        target.Add(helpers);
        target.Simulate = Playing;
        _window.Scenes.Activate(target);
        _markers.Scene = target;

        ClearSelection();
        if (!Playing) Say($"Opened the scene '{target.Name}'.");
    }

    private string UniqueSceneName(string baseName)
    {
        string name = baseName;
        for (int i = 2; _scenes.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)); i++) name = $"{baseName} {i}";
        return name;
    }

    /// <summary>A new scene with a sun, the physical sky and a ground to stand on, opened right away.</summary>
    private void NewScene()
    {
        var scene = new Scene { Name = UniqueSceneName("Scene"), Simulate = false };
        scene.Sun = new Sun { TimeOfDay = 15f, Latitude = -30f, Declination = 23f, NorthRotation = -40f, Intensity = 2.2f };
        scene.Skybox = new Skybox(new PhysicalSky { Clouds = new CloudLayer() });
        scene.Add(new GameObject(Utils.Primitives.Plane(40f, 40f, 1), Vector3.Zero)
        {
            Name = "Ground",
            Color = new Color4(0.55f, 0.58f, 0.55f, 1f),
        });
        _scenes.Add(scene);
        OpenScene(scene);
        Say($"Created the scene '{scene.Name}'. Rename it in the scene menu; scripts open it with Game.LoadScene(\"{scene.Name}\").");
    }

    private void RenameScene(Scene scene, string name)
    {
        name = name.Trim();
        if (name.Length == 0) return;
        if (_scenes.Any(s => s != scene && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            Say($"There is already a scene called '{name}'.", error: true);
            return;
        }
        Say($"Renamed the scene '{scene.Name}' to '{name}'.");
        scene.Name = name;
    }

    private void DeleteScene(Scene scene)
    {
        if (_scenes.Count <= 1)
        {
            Say("A game needs at least one scene.", error: true);
            return;
        }
        int index = _scenes.IndexOf(scene);
        if (scene == Scene) OpenScene(_scenes[index == 0 ? 1 : index - 1], keepVolume: false);
        _scenes.Remove(scene);
        scene.Dispose();
        Say($"Deleted the scene '{scene.Name}' (it is gone from the game at the next Compile).");
    }

    /// <summary>Puts the scene first: the game starts with it.</summary>
    private void MakeStartScene(Scene scene)
    {
        _scenes.Remove(scene);
        _scenes.Insert(0, scene);
        Say($"The game now starts with '{scene.Name}'.");
    }

    // ---- the scene menu (top of the Hierarchy) ----------------------------------------------------

    private void DrawSceneMenu()
    {
        var g = G;
        if (!g.BeginPopup("##scenemenu", 260f, 420f)) return;

        g.SeparatorText("Scenes");
        for (int i = 0; i < _scenes.Count; i++)
        {
            var scene = _scenes[i];
            string label = (i == 0 ? $"{scene.Name}  (start)" : scene.Name) + $"##scene{i}";
            if (g.Selectable(label, scene == Scene, IconKind.Scene))
            {
                if (Playing) Say("Stop playing to open another scene (scripts switch with Game.LoadScene).", error: true);
                else OpenScene(scene);
                g.CloseCurrentPopup();
            }
        }

        if (!Playing)
        {
            g.Spacing(2f);
            if (g.MenuItem("New scene", icon: IconKind.Plus))
            {
                NewScene();
                g.CloseCurrentPopup();
            }

            g.SeparatorText($"'{Scene.Name}'");
            if (!_renaming) _sceneName = Scene.Name;
            if (g.InputText("Name", ref _sceneName)) _renaming = true;
            if (_renaming && (g.LastInputEntered || g.Button("Rename", 0f, ButtonStyle.Primary)))
            {
                RenameScene(Scene, _sceneName);
                _renaming = false;
            }
            if (_scenes.IndexOf(Scene) > 0 && g.MenuItem("Start the game here", icon: IconKind.Play)) MakeStartScene(Scene);
            if (_scenes.Count > 1 && g.MenuItem("Delete scene", icon: IconKind.Trash))
            {
                DeleteScene(Scene);
                g.CloseCurrentPopup();
            }
        }
        g.TextWrapped("Scripts switch scenes with Game.LoadScene(\"Name\").", dim: true);
        g.EndPopup();
    }

    // ---- play across scenes -----------------------------------------------------------------------

    /// <summary>Remembers where every object of the scene is, so Stop can put it back.</summary>
    private void SnapshotScene(Scene scene)
    {
        if (!_playedScenes.Add(scene)) return;
        foreach (var obj in scene.Traverse())
        {
            if (obj.EditorOnly) continue;
            _snapshot[obj] = (obj.Position, obj.Rotation, obj.Scale, obj.Enabled, obj.Visible);
            _existedAtPlay.Add(obj);
        }
    }

    /// <summary>Game.LoadScene while playing in the editor: switch to the edited scene (restored on Stop) instead of building a copy.</summary>
    private bool LoadDuringPlay(string name)
    {
        var target = _scenes.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            Say($"Game.LoadScene: there is no scene called '{name}'.", error: true);
            return true;
        }
        _window.Game.ResetForNewScene();
        SnapshotScene(target);
        OpenScene(target, keepVolume: false);
        Say($"Game.LoadScene: now playing '{target.Name}'.");
        return true;
    }
}
