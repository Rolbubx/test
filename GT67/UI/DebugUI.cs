using GT67.Core;
using GT67.Gui;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;

namespace GT67.UI;

/// <summary>
/// The debug panels: stats, camera, scene hierarchy, inspector, lighting, water and render settings. In a game they
/// are one tabbed window (F2 shows or hides it); the editor docks the same panels into its side bars.
/// </summary>
public sealed class DebugUI : IDisposable
{
    private static readonly string[] Tabs = { "Stats", "Scene", "Inspector", "Lighting", "Water", "Render" };
    private static readonly IconKind[] TabIcons = { IconKind.Gear, IconKind.Cube, IconKind.Select, IconKind.Light, IconKind.Water, IconKind.Camera };

    private readonly Window _window;
    private float _smoothedFrameTime = 1f / 60f;
    private bool _vsync = true;
    private int _tab;

    /// <summary>Hidden by default in a game; F2 shows it.</summary>
    public bool Visible { get; set; }

    /// <summary>Currently selected object (also set by viewport picking).</summary>
    public GameObject? Selected { get; set; }

    public DebugUI(Window window) => _window = window;

    private Gui.Gui G => _window.Ui.Gui;

    /// <summary>Called every frame in a game (the editor draws the panels itself).</summary>
    public void Draw(float deltaTime)
    {
        _smoothedFrameTime = _smoothedFrameTime * 0.95f + deltaTime * 0.05f;
        if (!Visible) return;

        var g = G;
        var area = new UiRect(16f, 16f, 380f, MathF.Min(640f, g.DisplaySize.Y - 32f));
        if (g.Begin("Debug", area, WindowFlags.Floating))
        {
            _tab = g.TabBar("debugtabs", Tabs, _tab, TabIcons);
            g.Spacing(4f);
            switch (_tab)
            {
                case 0: DrawStats(); DrawCamera(); break;
                case 1: DrawHierarchy(); break;
                case 2: DrawInspector(); break;
                case 3: DrawLighting(); break;
                case 4: DrawWater(); break;
                default: DrawRender(); DrawVolume(); break;
            }
        }
        g.End();
    }

    // ---- panels ----------------------------------------------------------------------------------

    /// <summary>Picks a custom shader (a .shader file) for the object and edits its parameters.</summary>
    private void DrawShaderPicker(GameObject obj)
    {
        var g = G;
        var library = _window.Renderer.Custom;
        var names = new List<string> { "(normal)" };
        names.AddRange(library.Shaders.Select(s => s.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

        int index = Math.Max(0, names.FindIndex(n => string.Equals(n, obj.Material.CustomShader, StringComparison.OrdinalIgnoreCase)));
        if (g.Combo("Shader", ref index, names)) obj.Material.CustomShader = index == 0 ? "" : names[index];

        var shader = library.Get(obj.Material.CustomShader);
        if (shader == null)
        {
            if (obj.Material.CustomShader.Length > 0) g.TextStatus($"No shader named '{obj.Material.CustomShader}'.", error: true);
            return;
        }
        if (shader.Error != null) g.TextStatus(shader.Error, error: true);

        var material = obj.Material;
        foreach (var param in shader.Params)
        {
            var value = material.ShaderParams.TryGetValue(param.Name, out var own) ? own : param.Default;
            string name = param.Name;
            switch (param.Kind)
            {
                case ShaderParamKind.Float when param.Range is { } range:
                    g.Slider(name, value.X, range.X, range.Y, v => material.SetShaderParam(name, v));
                    break;
                case ShaderParamKind.Float:
                    g.Drag(name, value.X, 0.02f, v => material.SetShaderParam(name, v));
                    break;
                case ShaderParamKind.Bool:
                    g.Check(name, value.X > 0.5f, v => material.SetShaderParam(name, v ? 1f : 0f));
                    break;
                case ShaderParamKind.Color:
                    g.Rgb(name, new Color4(value.X, value.Y, value.Z, 1f), c => material.SetShaderParam(name, c.R, c.G, c.B));
                    break;
                case ShaderParamKind.Vec2:
                {
                    var v2 = new Vector2(value.X, value.Y);
                    if (g.DragVector2(name, ref v2, 0.02f)) material.SetShaderParam(name, v2.X, v2.Y);
                    break;
                }
                case ShaderParamKind.Texture:
                {
                    string path = material.ShaderTextures.TryGetValue(name, out var chosen) ? chosen : param.DefaultTexture;
                    if (g.InputText(name, ref path, "Assets/Textures/...png")) material.SetShaderTexture(name, path.Trim());
                    if (path.Length > 0 && !File.Exists(ModelCache.ResolvePath(path))) g.TextStatus("File not found: white is used.", error: true);
                    break;
                }
                default:
                    g.Drag3(name, new Vector3(value.X, value.Y, value.Z), 0.02f, v => material.SetShaderParam(name, v.X, v.Y, v.Z));
                    break;
            }
        }
    }

    public void DrawStats()
    {
        var g = G;
        float ms = _smoothedFrameTime * 1000f;
        g.SeparatorText("Performance");
        g.LabelValue("FPS", $"{1000f / MathF.Max(ms, 0.001f):0}");
        g.LabelValue("Frame time", $"{ms:0.00} ms");
        g.LabelValue("Objects drawn", $"{_window.Renderer.ObjectsDrawn}");
        g.LabelValue("Draw calls", $"{_window.Renderer.DrawCalls}");

        var renderer = _window.Renderer;
        g.SeparatorText("Culling");
        g.Check("Frustum culling", renderer.FrustumCulling, v => renderer.FrustumCulling = v);
        g.Check("Occlusion culling", renderer.OcclusionCulling, v => renderer.OcclusionCulling = v);
        g.LabelValue("Off screen", $"{renderer.ObjectsCulled}");
        g.LabelValue("Hidden behind others", $"{renderer.ObjectsOccluded}");
        g.LabelValue("Shadow casters skipped", $"{renderer.ShadowCastersCulled}");
        g.Spacing(2f);
        g.TextDim("F1 mouse capture | F2 this window | F5 shaders | RMB look");
    }

    public void DrawCamera()
    {
        var g = G;
        var cam = _window.Camera;
        g.SeparatorText("Camera");
        g.Drag3("Position", cam.Position, 0.05f, v => cam.Position = v);
        g.Drag("Yaw", cam.Yaw, 0.2f, v => cam.Yaw = v);
        g.Drag("Pitch", cam.Pitch, 0.2f, v => cam.Pitch = v);
        g.Slider("FOV", cam.Fov, 10f, 100f, v => cam.Fov = v, "0");
        g.Slider("Move speed", cam.MoveSpeed, 1f, 40f, v => cam.MoveSpeed = v, "0.0");
    }

    public void DrawHierarchy()
    {
        var g = G;
        foreach (var root in _window.Scene.Roots.ToArray()) DrawNode(root);
        if (g.Selectable("(nothing)", Selected == null)) Selected = null;
    }

    private void DrawNode(GameObject obj)
    {
        if (obj.EditorOnly) return;
        var g = G;
        bool open = g.TreeNode($"{obj.Name}##{obj.Id}", obj == Selected, out bool clicked, obj.Children.Count == 0,
            obj.Children.Count > 0 ? IconKind.Folder : IconKind.Cube, defaultOpen: true);
        if (clicked) Selected = obj;
        if (!open) return;

        foreach (var child in obj.Children.ToArray()) DrawNode(child);
        g.TreePop();
    }

    public void DrawInspector()
    {
        var g = G;
        var obj = Selected;
        if (obj == null || obj.IsDestroyed)
        {
            Selected = null;
            g.Spacing(10f);
            g.TextDim("Nothing selected.");
            g.TextWrapped("Click an object in the 3D view or pick anything in the Hierarchy: objects, lights, water, sounds, the Volume or the Environment.", dim: true);
            return;
        }

        g.Heading(obj.Name);
        g.TextDim($"Id {obj.Id}");
        g.Input("Name", obj.Name, v => obj.Name = v);
        g.Input("Tag", obj.Tag, v => obj.Tag = v);
        g.Check("Enabled", obj.Enabled, v => obj.Enabled = v);
        g.Check("Visible", obj.Visible, v => obj.Visible = v);

        g.SeparatorText("Transform");
        g.Drag3("Position", obj.Position, 0.05f, v => obj.Position = v);
        g.Drag3("Rotation", obj.EulerAngles, 0.5f, v => obj.EulerAngles = v);
        g.Drag3("Scale", obj.Scale, 0.02f, v => obj.Scale = v);
        g.Drag("Uniform scale", obj.UniformScale, 0.02f, v => obj.UniformScale = v, 0.01f, 100f);

        g.SeparatorText("Material");
        g.Rgba("Color", obj.Color, v => obj.Color = v);
        g.Slider("Opacity", obj.Opacity, 0f, 1f, v => obj.Opacity = v);
        g.Slider("Shininess", obj.Shininess, 1f, 256f, v => obj.Shininess = v, "0");
        g.Slider("Specular", obj.Material.SpecularStrength, 0f, 2f, v => obj.Material.SpecularStrength = v);
        var uv = obj.Material.UvScale;
        if (g.DragVector2("UV scale", ref uv, 0.05f)) obj.Material.UvScale = uv;
        g.Check("Unlit", obj.Unlit, v => obj.Unlit = v);
        DrawShaderPicker(obj);
        g.LabelValue("Diffuse", obj.Material.DiffuseMap?.Path is { } p ? Path.GetFileName(p) : "(model default)");

        g.SeparatorText("Rendering");
        g.Check("Cast shadow", obj.CastShadow, v => obj.CastShadow = v);
        g.Check("Receive shadow", obj.ReceiveShadow, v => obj.ReceiveShadow = v);
        g.Check("Wireframe", obj.Wireframe, v => obj.Wireframe = v);
        g.Check("Cull face", obj.CullFace, v => obj.CullFace = v);

        var b = obj.Bounds;
        g.SeparatorText("Bounds");
        g.LabelValue("Min", $"{b.Min.X:0.00}, {b.Min.Y:0.00}, {b.Min.Z:0.00}");
        g.LabelValue("Max", $"{b.Max.X:0.00}, {b.Max.Y:0.00}, {b.Max.Z:0.00}");
        g.LabelValue("Radius", $"{obj.BoundingSphere.Radius:0.00}");

        _window.Editor?.DrawInspectorExtras(obj);

        g.Spacing();
        if (g.IconTextButton(IconKind.Copy, "Clone", 110f))
        {
            var clone = obj.Clone();
            clone.Translate(1f, 0f, 0f);
            if (obj.Parent != null) obj.Parent.AddChild(clone);
            else _window.Scene.Add(clone);
            Selected = clone;
        }
        g.SameLine();
        if (g.IconTextButton(IconKind.Trash, "Destroy", 110f, ButtonStyle.Danger))
        {
            obj.Destroy();
            Selected = null;
        }
    }

    /// <summary>The debug window's Lighting tab: the environment, the sounds and every light.</summary>
    public void DrawLighting()
    {
        var g = G;
        var scene = _window.Scene;
        DrawEnvironment();

        if (g.CollapsingHeader("Sounds", defaultOpen: false))
        {
            g.PushId("sounds");
            g.TextDim("Drop a .wav, .ogg or .mp3 onto the window to add one.");
            g.Slider("Master volume", Sound.MasterVolume, 0f, 1.5f, v => Sound.MasterVolume = v);
            for (int i = 0; i < scene.Sounds.Count; i++)
            {
                var sound = scene.Sounds[i];
                g.PushId(i);
                if (g.CollapsingHeader($"{sound.Name}##sound", defaultOpen: false) && !DrawSoundSettings(sound))
                {
                    g.PopId();
                    break;
                }
                g.PopId();
            }
            if (g.Button("Add empty sound")) scene.Add(new Sound { Name = $"Sound {scene.Sounds.Count + 1}" });
            g.PopId();
        }

        if (g.CollapsingHeader("Performance", defaultOpen: false))
        {
            g.PushId("perf");
            DrawPerformance();
            g.PopId();
        }

        for (int i = 0; i < scene.PointLights.Count; i++)
        {
            var light = scene.PointLights[i];
            g.PushId(i);
            bool open = g.CollapsingHeader($"{light.Name} {i + 1}##light", defaultOpen: false);
            if (open)
            {
                g.Check("Enabled", light.Enabled, v => light.Enabled = v);
                g.Drag3("Position", light.Position, 0.05f, v => light.Position = v);
                LightColorIntensity(light);
                g.Slider("Range", light.Range, 1f, 60f, v => light.Range = v, "0.0");
                if (g.IconTextButton(IconKind.Trash, "Remove light", 140f, ButtonStyle.Danger))
                {
                    scene.PointLights.RemoveAt(i);
                    g.PopId();
                    break;
                }
            }
            g.PopId();
        }

        if (scene.Spot is { } spot && g.CollapsingHeader("Spot light", defaultOpen: false))
        {
            g.PushId("spot");
            g.Check("Enabled", spot.Enabled, v => spot.Enabled = v);
            g.Drag3("Position", spot.Position, 0.05f, v => spot.Position = v);
            g.Drag3("Direction", spot.Direction, 0.01f, v => spot.Direction = v);
            LightColorIntensity(spot);
            g.Slider("Inner angle", spot.InnerAngle, 1f, 80f, v => spot.InnerAngle = v, "0");
            g.Slider("Outer angle", spot.OuterAngle, 1f, 89f, v => spot.OuterAngle = v, "0");
            g.Slider("Range", spot.Range, 1f, 80f, v => spot.Range = v, "0.0");
            g.PopId();
        }
    }

    /// <summary>The scene's environment: ambient light, sun, sky, clouds, rain and fog.</summary>
    public void DrawEnvironment()
    {
        var g = G;
        var scene = _window.Scene;

        if (g.CollapsingHeader("Ambient"))
        {
            g.Rgb("Colour", scene.AmbientColor, v => scene.AmbientColor = v);
            g.Slider("Intensity", scene.AmbientIntensity, 0f, 2f, v => scene.AmbientIntensity = v);
        }

        if (scene.Sun is { } sun && g.CollapsingHeader("Sun"))
        {
            g.PushId("sun");
            g.Check("Enabled", sun.Enabled, v => sun.Enabled = v);
            g.Check("Follow time of day", sun.AutoPosition, v => sun.AutoPosition = v);
            if (sun.AutoPosition)
            {
                g.Slider("Time of day", sun.TimeOfDay, 0f, 24f, v => sun.TimeOfDay = v, "0.00");
                g.Slider("Time speed", sun.TimeSpeed, 0f, 2f, v => sun.TimeSpeed = v);
                g.Slider("Latitude", sun.Latitude, -90f, 90f, v => sun.Latitude = v, "0");
                g.Slider("Season", sun.Declination, -23.5f, 23.5f, v => sun.Declination = v, "0.0");
                g.Slider("Compass", sun.NorthRotation, -180f, 180f, v => sun.NorthRotation = v, "0");
            }
            else
            {
                g.Drag3("Direction", sun.Direction, 0.01f, v => sun.Direction = v);
            }
            g.TextDim($"Elevation {sun.Elevation:0.0}°, azimuth {sun.Azimuth:0.0}°");

            LightColorIntensity(sun);
            g.Check("Atmospheric colour", sun.AtmosphericLighting, v => sun.AtmosphericLighting = v);
            g.Slider("Disc radius", sun.AngularRadius, 0.1f, 5f, v => sun.AngularRadius = v);
            g.Slider("Disc brightness", sun.DiscBrightness, 0f, 200f, v => sun.DiscBrightness = v, "0");
            g.Check("Cast shadow", sun.CastShadow, v => sun.CastShadow = v);
            g.Slider("Shadow size", sun.ShadowSize, 5f, 100f, v => sun.ShadowSize = v, "0");
            g.Slider("Shadow bias", sun.ShadowBias, 0.0001f, 0.01f, v => sun.ShadowBias = v, "0.0000");
            g.PopId();
        }

        if (scene.Skybox?.Physical is { } sky && g.CollapsingHeader("Sky"))
        {
            g.PushId("sky");
            g.Slider("Brightness", sky.Brightness, 1f, 60f, v => sky.Brightness = v, "0.0");
            g.Slider("Haze", sky.MieCoefficient * 1000f, 2f, 100f, v => sky.MieCoefficient = v / 1000f, "0.0");
            g.Slider("Haze anisotropy", sky.MieAnisotropy, 0f, 0.98f, v => sky.MieAnisotropy = v);
            g.Slider("Ambient from sky", sky.AmbientScale, 0f, 2f, v => sky.AmbientScale = v);
            g.Check("Stars", sky.Stars, v => sky.Stars = v);
            g.PopId();

            if (sky.Clouds is { } clouds && g.CollapsingHeader("Clouds"))
            {
                g.PushId("clouds");
                g.Check("Enabled", clouds.Enabled, v => clouds.Enabled = v);
                g.Slider("Coverage", clouds.Coverage, 0f, 1f, v => clouds.Coverage = v);
                g.Slider("Density", clouds.Density, 0.1f, 4f, v => clouds.Density = v);
                g.Slider("Altitude", clouds.Altitude, 200f, 6000f, v => clouds.Altitude = v, "0");
                g.Slider("Thickness", clouds.Thickness, 300f, 6000f, v => clouds.Thickness = v, "0");
                g.Check("Cast shadows", clouds.CastShadows, v => clouds.CastShadows = v);
                g.Slider("Shadow strength", clouds.ShadowStrength, 0f, 1f, v => clouds.ShadowStrength = v);
                g.Slider("Wind speed", clouds.WindSpeed, 0f, 200f, v => clouds.WindSpeed = v, "0");
                g.Slider("Edge detail", clouds.DetailStrength, 0f, 0.95f, v => clouds.DetailStrength = v);
                g.Slider("Variation", clouds.Variation, 0f, 1f, v => clouds.Variation = v);
                g.Slider("Evolution", clouds.Evolution, 0f, 10f, v => clouds.Evolution = v, "0.0");
                g.Slider("Weather size (m)", clouds.WeatherSize, 2000f, 150000f, v => clouds.WeatherSize = v, "0");
                int seed = clouds.Seed;
                if (g.DragInt("Seed", ref seed)) clouds.Seed = seed;
                g.Slider("Sun light", clouds.SunIntensity, 0f, 6f, v => clouds.SunIntensity = v);
                g.Slider("Ambient light", clouds.AmbientIntensity, 0f, 4f, v => clouds.AmbientIntensity = v);
                g.PopId();
            }
        }

        if (g.CollapsingHeader("Rain"))
        {
            g.PushId("rain");
            if (scene.Rain is { } rain)
            {
                g.Check("Enabled", rain.Enabled, v => rain.Enabled = v);
                g.Slider("Intensity", rain.Intensity, 0f, 1f, v => rain.Intensity = v);
                g.Slider("Lasts (s, 0 = forever)", rain.Duration, 0f, 600f, v => rain.Duration = v, "0");
                g.Slider("Pause before it returns (s)", rain.Pause, 0f, 600f, v => rain.Pause = v, "0");
                g.Slider("Fade time (s)", rain.FadeTime, 1f, 60f, v => rain.FadeTime = v, "0");
                g.Slider("Fall speed", rain.Speed, 4f, 30f, v => rain.Speed = v, "0.0");
                g.Slider("Streak length", rain.Length, 0.1f, 2f, v => rain.Length = v);
                g.Slider("Wind slant", rain.Slant, -0.5f, 0.5f, v => rain.Slant = v);
                g.Slider("Brightness", rain.Brightness, 0f, 3f, v => rain.Brightness = v);
                g.Slider("Splashes", rain.Splashes, 0f, 3f, v => rain.Splashes = v);
                g.Slider("Darken", rain.Darken, 0f, 1f, v => rain.Darken = v);
                g.TextDim(rain.Finished ? "The rain is over." : $"Rain time {rain.Elapsed:0} s, intensity now {rain.Amount:0.00}");
                if (g.Button("Restart rain")) rain.Restart();
                if (g.Button("Remove rain")) scene.Rain = null;
            }
            else if (g.Button("Add rain"))
            {
                scene.Rain = new Rain();
            }
            g.Slider("Fog", scene.Fog, 0f, 0.02f, v => scene.Fog = v, "0.0000");
            g.PopId();
        }
    }

    /// <summary>Every setting of one sound. Returns false if the sound was removed from the scene.</summary>
    public bool DrawSoundSettings(Sound sound)
    {
        var g = G;
        g.Input("Name", sound.Name, v => sound.Name = v);
        g.Input("File", sound.File, v => sound.File = v);
        g.Check("Enabled", sound.Enabled, v => sound.Enabled = v);
        g.Slider("Volume", sound.Volume, 0f, 2f, v => sound.Volume = v);
        g.Slider("Pitch", sound.Pitch, 0.25f, 3f, v => sound.Pitch = v);
        g.Check("Loop", sound.Loop, v => sound.Loop = v);
        g.Check("Play on start", sound.PlayOnStart, v => sound.PlayOnStart = v);
        g.Check("3D (from a place in the world)", sound.Spatial, v => sound.Spatial = v);
        if (sound.Spatial)
        {
            g.Drag3("Position", sound.Position, 0.05f, v => sound.Position = v);
            g.Slider("Full volume within", sound.MinDistance, 0.1f, 50f, v => sound.MinDistance = v, "0.0");
            g.Slider("Silent beyond", sound.MaxDistance, 1f, 300f, v => sound.MaxDistance = v, "0");
        }
        g.TextDim(sound.Length > 0f ? $"{sound.Length:0.0} s" + (sound.IsPlaying ? $", playing {sound.Time:0.0} s" : "") : "File not found or not readable.");
        if (g.IconTextButton(sound.IsPlaying ? IconKind.Stop : IconKind.Play, sound.IsPlaying ? "Stop" : "Play", 100f))
        {
            if (sound.IsPlaying) sound.Stop(); else sound.Play();
        }
        g.SameLine();
        if (g.IconTextButton(IconKind.Trash, "Remove", 110f, ButtonStyle.Danger))
        {
            _window.Scene.Remove(sound);
            return false;
        }
        return true;
    }

    /// <summary>Quality and speed settings of the renderer.</summary>
    public void DrawPerformance()
    {
        var g = G;
        var renderer = _window.Renderer;
        g.Check("Automatic quality", renderer.AdaptiveQuality, v => renderer.AdaptiveQuality = v);
        g.Slider("Quality (0 best, 3 fastest)", renderer.Quality, 0f, 3f, v => renderer.Quality = (int)MathF.Round(v), "0");
        g.Check("Puddle reflections", renderer.ReflectionsEnabled, v => renderer.ReflectionsEnabled = v);
        g.TextDim($"{1f / MathF.Max(renderer.AverageFrameTime, 0.001f):0} fps, {renderer.AverageFrameTime * 1000f:0.0} ms per frame");
    }

    private void LightColorIntensity(Light light)
    {
        var g = G;
        g.Rgb("Colour", light.Color, v => light.Color = v);
        g.Slider("Intensity", light.Intensity, 0f, 10f, v => light.Intensity = v);
    }

    public void DrawWater()
    {
        var g = G;
        if (_window.Scene.Waters.Count == 0)
        {
            g.Spacing(10f);
            g.TextDim("There is no water in this scene.");
            return;
        }

        for (int i = 0; i < _window.Scene.Waters.Count; i++)
        {
            var water = _window.Scene.Waters[i];
            g.PushId(i);
            if (g.CollapsingHeader($"{water.Name} {i + 1}##water")) DrawWaterSettings(water);
            g.PopId();
        }
    }

    /// <summary>Every setting of one body of water: place and size, level of detail, waves, look, reflections, foam.</summary>
    public void DrawWaterSettings(Water water)
    {
        var g = G;
        {
            {
                g.Check("Enabled", water.Enabled, v => water.Enabled = v);

                g.SeparatorText("Place and size");
                g.Drag("Level (Y)", water.Level, 0.02f, v => water.Level = v);
                g.Check("Reach horizon (sea)", water.ReachHorizon, v => water.ReachHorizon = v);
                if (water.ReachHorizon)
                {
                    g.TextDim("A sea around the camera out to the horizon: the rectangle below is not used.");
                }
                else
                {
                    g.Drag("Centre X", water.Position.X, 0.1f, v => water.Position = water.Position with { X = v });
                    g.Drag("Centre Z", water.Position.Z, 0.1f, v => water.Position = water.Position with { Z = v });
                    g.Drag("Width (X)", water.Width, 0.1f, v => water.Width = v, 0.5f, 10000f);
                    g.Drag("Depth (Z)", water.Depth, 0.1f, v => water.Depth = v, 0.5f, 10000f);
                }

                g.SeparatorText("Level of detail");
                g.Slider("Tile size", water.TileSize, 4f, 64f, v => water.TileSize = v, "0");
                g.Slider("Detail", water.Detail, 8f, 128f, v => water.Detail = (int)MathF.Round(v), "0");
                g.Slider("LOD distance", water.LodDistance, 5f, 200f, v => water.LodDistance = v, "0");
                g.TextDim($"{water.Nodes.Count} tile(s) drawn; tiles double in size at every doubling of the LOD distance.");

                g.SeparatorText("Waves");
                g.Slider("Wave height", water.WaveHeight, 0f, 1f, v => water.WaveHeight = v);
                g.Slider("Wave length", water.WaveLength, 0.5f, 30f, v => water.WaveLength = v, "0.0");
                g.Slider("Choppiness", water.Choppiness, 0f, 1f, v => water.Choppiness = v);
                g.Slider("Direction spread", water.DirectionSpread, 0f, 90f, v => water.DirectionSpread = v, "0");
                g.Slider("Speed", water.Speed, 0f, 4f, v => water.Speed = v);
                g.Slider("Ripple strength", water.RippleStrength, 0f, 2f, v => water.RippleStrength = v);
                g.Slider("Ripple scale", water.RippleScale, 0.05f, 2f, v => water.RippleScale = v);

                g.SeparatorText("Look");
                g.Rgb("Shallow colour", water.ShallowColor, v => water.ShallowColor = v);
                g.Rgb("Deep colour", water.DeepColor, v => water.DeepColor = v);
                g.Drag3("Absorption", water.Absorption, 0.005f, v => water.Absorption = v);
                g.Slider("Depth colour rate", water.DepthColorRate, 0.02f, 2f, v => water.DepthColorRate = v);
                g.Slider("Sun glint", water.SpecularStrength, 0f, 4f, v => water.SpecularStrength = v);
                g.Slider("Glint sharpness", water.Shininess, 10f, 1000f, v => water.Shininess = v, "0");
                g.Slider("Sparkle", water.Sparkle, 0f, 3f, v => water.Sparkle = v);
                g.Slider("Crest light", water.CrestLight, 0f, 3f, v => water.CrestLight = v);

                g.SeparatorText("Reflection / refraction");
                g.Check("Reflections", water.ReflectionEnabled, v => water.ReflectionEnabled = v);
                g.Slider("Reflection size", water.ReflectionScale, 0.25f, 1f, v => water.ReflectionScale = v);
                g.Slider("Distortion", water.ReflectionDistortion, 0f, 0.2f, v => water.ReflectionDistortion = v, "0.000");
                g.Slider("Refraction", water.RefractionStrength, 0f, 0.3f, v => water.RefractionStrength = v, "0.000");
                g.Slider("Fresnel F0", water.FresnelF0, 0f, 0.2f, v => water.FresnelF0 = v, "0.000");

                g.SeparatorText("Foam and shore");
                g.Slider("Foam", water.FoamAmount, 0f, 2f, v => water.FoamAmount = v);
                g.Slider("Shore foam width", water.ShoreFoamWidth, 0.05f, 2f, v => water.ShoreFoamWidth = v);
                g.Slider("Edge softness", water.EdgeSoftness, 0f, 1.5f, v => water.EdgeSoftness = v);
                g.Slider("Edge fade", water.EdgeFade, 0f, 30f, v => water.EdgeFade = v, "0.0");
            }
        }
    }

    /// <summary>The "Main Volume": tone mapping and every post-processing effect with all of its settings.</summary>
    public void DrawVolume()
    {
        var g = G;
        var post = _window.Renderer.Post;
        g.SeparatorText("Tone mapping");
        g.Check("Tone map + gamma", post.Enabled, v => post.Enabled = v);
        g.Slider("Exposure", post.Exposure, 0.1f, 4f, v => post.Exposure = v);
        g.Choose("Operator", post.ToneMap, v => post.ToneMap = v);

        g.SeparatorText("Post-processing effects");
        foreach (var effect in post.Effects)
        {
            g.PushId(effect.Name);
            if (g.CollapsingHeader(effect.Name, defaultOpen: true)) ParameterEditor.Draw(g, effect);
            g.PopId();
        }
    }

    public void DrawRender()
    {
        var g = G;
        var renderer = _window.Renderer;
        var post = renderer.Post;

        g.SeparatorText("Scene");
        g.Check("Wireframe (all)", renderer.Wireframe, v => renderer.Wireframe = v);
        g.Check("Skybox", renderer.SkyboxEnabled, v => renderer.SkyboxEnabled = v);
        g.Check("Shadows", renderer.ShadowsEnabled, v => renderer.ShadowsEnabled = v);
        g.Rgb("Clear colour", renderer.ClearColor, v => renderer.ClearColor = v);

        g.SeparatorText("Display");
        g.LabelValue("Anti-aliasing", renderer.MsaaSamples > 0 ? $"MSAA {renderer.MsaaSamples}x" : "off");
        if (g.Toggle("VSync", ref _vsync)) _window.VSync = _vsync ? VSyncMode.On : VSyncMode.Off;

        g.SeparatorText("Performance");
        DrawPerformance();

        g.Spacing(4f);
        if (g.IconTextButton(IconKind.Refresh, "Reload shaders (F5)")) _window.ReloadShaders();
    }

    public void Dispose() { }
}
