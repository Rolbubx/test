using GT67.Core;
using GT67.Effects;
using GT67.Generated;
using GT67.UI;
using GT67.Utils;
using OpenTK.Mathematics;

// ---- Mode ---------------------------------------------------------------------------------------------------
// true : open the map editor. Drop .fbx / .gltf / .glb / .obj files onto the window, drag shapes out of the Assets
//        window, move objects with the gizmo, attach C# scripts (Scripts/*.cs, compiled live), sculpt and paint the
//        terrain, press Play to try it, and press Compile to write the game into Generated/GeneratedGame/ (several
//        files; models, textures and sounds are shipped with it).
// false: run the game. With "--generated" it builds the game the editor wrote (GameGenerator.Build, including the
//        game UI designed in the editor); otherwise it builds the demo scene at the bottom of this file.
//        "--editor --generated" opens the generated game in the editor to keep working on it.
// The editor opens the last compiled map ("--demo" for the demo scene); the game starts from the demo scene (and
// its HUD/menu) unless "--generated" is given (the Game build always runs the compiled map).
// The build configuration picks the default: "Editor" opens the editor, "Game" runs the game (Debug/Release: below).
#if EDITOR
bool IsEditor = true;
#elif GAME
bool IsEditor = false;
#else
bool IsEditor = false;
#endif

// Optional command line: "--frames N" closes the window after N frames (smoke tests), "--editor" / "--game"
// override IsEditor, "--debug" turns on the temporary DebugFlag.On (the editor keeps it in its settings instead).
// "--camera x,y,z,tx,ty,tz" fixes the view (from x,y,z towards tx,ty,tz) and "--screenshot file.png" saves the last
// frame of a "--frames" run, e.g. to compare a shader change before and after. "--scene Name" starts the compiled
// game (--generated) in that scene instead of the first one.
int maxFrames = 0;
bool noVsync = false, dry = false;
var offList = new List<string>();
string? playSound = null;
bool noSky = false, noShadow = false, noReflect = false, generated = false, exportDemo = false, demo = false;
#if GAME
generated = true;   // the shipped game is the one the editor compiled (the demo scene is only a fallback)
_ = exportDemo;     // "--export-demo" needs the editor, which the Game build leaves out
#endif
string? screenshot = null, startScene = null;
float[]? cameraView = null;
var argv = Environment.GetCommandLineArgs();
for (int i = 1; i < argv.Length; i++)
{
    if (argv[i] == "--frames" && i + 1 < argv.Length && int.TryParse(argv[i + 1], out int n)) maxFrames = n;
    if (argv[i] == "--novsync") noVsync = true;
    if (argv[i] == "--dry") dry = true;   // no rain, to compare speed
    if (argv[i] == "--playsound" && i + 1 < argv.Length) playSound = argv[i + 1];   // smoke-test the sound system
    if (argv[i] == "--off" && i + 1 < argv.Length) offList.Add(argv[i + 1]);   // switch a post effect off by name, to compare speed
    if (argv[i] == "--nosky") noSky = true;
    if (argv[i] == "--noreflect") noReflect = true;
    if (argv[i] == "--noshadow") noShadow = true;
    if (argv[i] == "--generated") generated = true;
    if (argv[i] == "--demo") demo = true;   // the editor: open the demo scene instead of the last compiled map
    if (argv[i] == "--export-demo") exportDemo = true;   // write the demo scene through the exporter (a test of Compile)
    if (argv[i] == "--editor") IsEditor = true;
    if (argv[i] == "--game") IsEditor = false;
    if (argv[i] == "--debug") DebugFlag.On = true;
    if (argv[i] == "--screenshot" && i + 1 < argv.Length) screenshot = argv[i + 1];   // with --frames: save the last frame
    if (argv[i] == "--camera" && i + 1 < argv.Length) cameraView = ParseFloats(argv[i + 1]);   // "x,y,z,targetX,targetY,targetZ"
    if (argv[i] == "--scene" && i + 1 < argv.Length) startScene = argv[i + 1];   // with --generated: start in this scene
}
// The editor opens the map saved by the last Compile (the demo scene only when nothing was compiled yet, or with --demo).
if (IsEditor && !demo) generated = true;


using var window = new Window(IsEditor ? "GT67 Editor" : "GT67", 1280, 720, maxFrames) { IsEditor = IsEditor, UncappedFps = noVsync, ScreenshotPath = screenshot };
window.Initialize = w =>
{
    SetupPostProcessing(w, IsEditor);

    // "--camera": a fixed view for screenshots; camera scripts leave the camera alone, as in the editor's free view.
    if (cameraView is { Length: 6 } c)
    {
        w.Game.ViewDetached = true;
        w.Camera.Position = new Vector3(c[0], c[1], c[2]);
        w.Camera.LookAt(new Vector3(c[3], c[4], c[5]));
    }

#if !GAME
    if (exportDemo)
    {
        BuildDemoScene(w);
        if (playSound != null) w.Scene.Add(new Sound(playSound) { Name = "Test tone", Loop = true, PlayOnStart = true, Spatial = true, Position = new Vector3(2f, 1f, 0f), Volume = 0.3f });
        w.Scene.Volume.CaptureFrom(w.Renderer.Post);
        var result = new GT67.Editor.Export.GameExporter(new[] { w.Scene }, GT67.Editor.ProjectPaths.Locate()!, w.GameUi).Generate();
        Log.Info($"Exported: {string.Join(", ", result.Files)}; {result.Objects} objects, {result.CopiedFiles} asset files.");
        foreach (var warning in result.Warnings) Log.Warn(warning);
        w.Close();
        return;
    }
#endif

    // "--generated": the game the editor wrote with Compile (Generated/GeneratedGame) instead of the demo scene.
    if (generated && GameGenerator.IsGenerated)
    {
        Log.Info("Building the game from Generated/GeneratedGame.");
        GameGenerator.Build(w);
        if (startScene != null) w.Scenes.LoadNow(startScene);
        return;
    }

    // The editor and the game both start from the demo scene.
    BuildDemoScene(w);
    if (dry) w.Scene.Rain = null;
    if (playSound != null) Sound.PlayOnce(playSound, 0.5f);
    foreach (var name in offList) foreach (var e in w.Renderer.Post.Effects) if (e.Name == name) e.Enabled = false;
    if (noReflect) { w.Renderer.ReflectionsEnabled = false; foreach (var water in w.Scene.Waters) water.ReflectionEnabled = false; }
    if (noSky) w.Renderer.SkyboxEnabled = false;
    if (noShadow) w.Renderer.ShadowsEnabled = false;
    BuildDemoUi(w);   // in the editor too, so the UI tab shows (and Compile keeps) the HUD and the menu
};
window.Run();
return 0;

static float[] ParseFloats(string text) =>
    text.Split(',').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();

// ---- Post-processing: effects are added in code and run in this order (HDR first, then tone map, then LDR) ------
static void SetupPostProcessing(Window window, bool editor)
{
    // Every effect is added in both modes, so the editor's Main Volume shows all of them. The editor starts with the
    // distracting ones (depth of field, chromatic aberration, grain) switched off; a compiled map stores the switches.
    var post = window.Renderer.Post;
    post.Add(new VolumetricFogEffect());
    post.Add(new DepthOfFieldEffect { FocusDistance = 14f, FocusRange = 12f, MaxBlur = 6f, Enabled = !editor });
    post.Add(new BloomEffect { Threshold = 1.0f, Intensity = 0.35f });
    post.Add(new SunRaysEffect());
    post.Add(new RainEffect());
    post.Add(new ChromaticAberrationEffect { Strength = 0.004f, Enabled = !editor });
    post.Add(new ColorCorrectionEffect { Contrast = 1.12f, Saturation = 1.12f, Vibrance = 0.3f, Temperature = 0.03f });
    post.Add(new SharpenEffect());
    post.Add(new VignetteEffect { Intensity = editor ? 0.25f : 0.5f });
    post.Add(new FilmGrainEffect { Strength = 0.03f, Enabled = !editor });

    // Every effect parameter can also be read and set by name (for scripts, config files, key bindings...).
    post.Set("Bloom", "Intensity", 0.4f);
    post.Set("ColorCorrection", "Lift", (0.01f, 0.01f, 0.03f));
}

// ---- The demo scene (what the editor and the game start with) ----------------------------------------------------
static void BuildDemoScene(Window window)
{
    var scene = window.Scene;
    scene.Rain = new Rain { Intensity = 0.7f };   // set to null (or Intensity 0) for dry weather

    // --- lights -------------------------------------------------------------------------------
    // The sun has a place in the sky: time of day, latitude, season and compass rotation (here a late-afternoon
    // sun in front of the default camera). Set sun.Direction instead to aim it by hand.
    scene.Sun = new Sun { TimeOfDay = 16f, Latitude = -30f, Declination = 23f, NorthRotation = -40f, Intensity = 2.2f };
    scene.AddLight(new PointLight(new Vector3(-4f, 1.6f, 2f), Color4.OrangeRed, intensity: 4f, range: 10f) { Name = "Warm light" });
    scene.AddLight(new PointLight(new Vector3(4f, 1.6f, 1f), Color4.DeepSkyBlue, intensity: 4f, range: 10f) { Name = "Cool light" });
    scene.Spot = new SpotLight(new Vector3(0, 6, 4), new Vector3(0, -1, -0.6f)) { Intensity = 4f, Enabled = false };

    // --- sky: cubemap from Assets/Skybox if present, otherwise the physically based atmosphere ---
    // The physical sky is just the atmosphere: it tints and dims scene.Sun and sets the ambient light to match.
    var cubemap = Cubemap.TryFromFolder(ModelCache.ResolvePath("Assets/Skybox"));
    if (cubemap != null)
    {
        scene.Skybox = new Skybox(cubemap);
    }
    else
    {
        Log.Info("No skybox images in Assets/Skybox; using the physically based sky.");
        scene.Skybox = new Skybox(new PhysicalSky { Clouds = new CloudLayer() });
    }

    // --- textures (procedural fallback when the file is missing) -------------------------------
    var floorTexture = ModelCache.Register("procedural:floor", Texture.Checkerboard(512, 16, (200, 200, 200), (120, 125, 120)));
    Texture crateTexture;
    if (Exists("Assets/Textures/crate.png"))
    {
        crateTexture = ModelCache.GetTexture("Assets/Textures/crate.png");
    }
    else
    {
        Log.Warn("Assets/Textures/crate.png not found; using a procedural checkerboard.");
        crateTexture = ModelCache.Register("procedural:crate", Texture.Checkerboard(256, 4, (230, 160, 70), (110, 70, 30)));
    }

    // --- objects ------------------------------------------------------------------------------
    // Ground with a lake basin: flat at y = 0, dipping to about -2 m around (lakeX, lakeZ).
    const float lakeX = 9f, lakeZ = -6f, lakeRadius = 7f, waterLevel = -0.3f;
    float GroundHeight(float x, float z)
    {
        float r = MathF.Sqrt((x - lakeX) * (x - lakeX) + (z - lakeZ) * (z - lakeZ)) / lakeRadius;
        float s = Math.Clamp(r, 0f, 1f);
        return -2f * (1f - s * s * (3f - 2f * s));   // smoothstep bowl
    }
    var ground = new GameObject(Primitives.Heightmap(50, 50, 160, GroundHeight, (x, z, h) =>
    {
        // Sandy lake bed that darkens with depth; plain checkerboard everywhere else.
        float bed = Math.Clamp((-0.05f - h) * 3f, 0f, 1f);
        var sand = new Vector4(0.82f, 0.72f, 0.5f, 1f) * (1f - 0.55f * Math.Clamp(-h / 2f, 0f, 1f));
        return Vector4.Lerp(Vector4.One, new Vector4(sand.X, sand.Y, sand.Z, 1f), bed);
    }), 0, 0, 0) { Name = "Ground", CastShadow = false };
    ground.SetTexture(floorTexture);
    ground.Material.UvScale = new Vector2(1, 1);
    ground.Material.SpecularStrength = 0.1f;

    var crate = new GameObject(Primitives.Cube(), 2, 0.5f, -3) { Name = "Crate", Tag = "spinner", Color = Color4.White };
    crate.SetTexture(crateTexture);
    crate.AddScript(new Spin { DegreesPerSecond = 40f }); // a script from Scripts/Spin.cs

    var moon = new GameObject(Primitives.Sphere(0.18f, 16), 0, 1.1f, 0) { Name = "Moon", Color = Color4.LightGray };
    crate.AddChild(moon); // child follows the crate's transform

    var model = Model.TryLoad("Assets/Models/tree.obj")
                ?? Model.TryLoad("Assets/Models/pyramid.obj")
                ?? Model.FromMesh(Primitives.Cone(0.6f, 1.6f, 24));
    var landmark = new GameObject(model, -4, 0, -4) { Name = "Model" };
    landmark.UniformScale = 2f;

    var sphere = new GameObject(Primitives.Sphere(0.75f, 48), -2.5f, 0.75f, -1) { Name = "Sphere", Color = Color4.CornflowerBlue };
    sphere.Shininess = 128f;
    var cylinder = new GameObject(Primitives.Cylinder(0.5f, 1.4f, 32), 0.5f, 0.7f, -1) { Name = "Cylinder", Color = Color4.LimeGreen };
    var torus = new GameObject(Primitives.Torus(0.6f, 0.2f), 3.5f, 0.8f, 0.5f) { Name = "Torus", Color = Color4.HotPink };
    torus.RotateX(60f);
    torus.OnUpdate = (self, dt) => self.RotateZ(25f * dt);
    torus.SetShader("Pulse");   // a custom shader: Shaders/Custom/Pulse.shader
    var cone = new GameObject(Primitives.Cone(0.5f, 1.2f, 32), -1.5f, 0.6f, 2.5f) { Name = "Cone", Color = Color4.Gold };
    // The player: a capsule that walks (WASD / arrows, Shift runs, Space jumps) with Scripts/PlayerController.cs; its
    // collision sphere sits in the capsule's feet. The game camera (Scripts/GameCamera.cs) follows the "Player" tag.
    const float playerRadius = 0.35f, playerHeight = 1.8f;
    var player = new GameObject(Primitives.Capsule(playerRadius, playerHeight, 24), 1.5f, playerHeight * 0.5f, 2.5f)
        { Name = "Player", Tag = "Player", Color = Color4.MediumPurple };
    player.AddScript(new PlayerController { Radius = playerRadius, SphereOffset = playerRadius - playerHeight * 0.5f });

    var gameCamera = new GameObject(Primitives.CameraBody(), 1.5f, 3.4f, 8.5f)
        { Name = "Camera", Color = new Color4(0.2f, 0.22f, 0.26f, 1f), Collision = CollisionMode.None, CastShadow = false };
    gameCamera.LookAt(player);
    gameCamera.AddScript(new GameCamera());   // third person; change Mode in the editor's Inspector

    var glass = new GameObject(Primitives.Sphere(0.9f, 32), 0, 0.9f, 4f) { Name = "Glass", Color = Color4.Cyan, Opacity = 0.35f, CastShadow = false };

    var axes = new GameObject(Primitives.Axes(1.5f), 0, 0.02f, 0) { Name = "Axes", Unlit = true, CastShadow = false, Collision = CollisionMode.None };

    // Small unlit markers showing where the point lights are.
    foreach (var light in scene.PointLights)
    {
        var marker = new GameObject(Primitives.Sphere(0.12f, 12), light.Position)
            { Name = light.Name + " marker", Color = light.Color, Unlit = true, CastShadow = false, Collision = CollisionMode.None };
        scene.Add(marker);
    }

    scene.Add(ground, crate, landmark, sphere, cylinder, torus, cone, player, gameCamera, glass, axes);

    // --- water: reflections, refraction, waves, foam and soft shores; every setting is a property ---------
    var lake = new Water(lakeX, waterLevel, lakeZ, 2f * lakeRadius, 2f * lakeRadius) { Name = "Lake" };
    scene.Add(lake);

    // --- grass: blades scattered on the ground, not in the lake or on its wet bed ------------------------------------
    var random = new Random(7);
    scene.Grass = new GrassLayer();
    scene.Grass.Scatter(new Vector2(-24f, -24f), new Vector2(24f, 24f), 40f, (x, z) =>
    {
        float h = GroundHeight(x, z);
        if (h < -0.12f) return null;                                   // under or at the water's edge
        if (MathF.Abs(x) < 5f && z > -5f && z < 6f) return null;       // keep the showcase objects in the middle clear
        float edge = MathF.Max(MathF.Abs(x), MathF.Abs(z));            // thins out towards the edge of the ground
        return random.NextDouble() < 1.0 - Math.Clamp((edge - 18f) / 6f, 0f, 1f) ? h : null;
    });

    // A rock half in the water, to show the shoreline foam and the refraction of submerged things.
    var rock = new GameObject(Primitives.Sphere(0.9f, 24), lakeX - 2.6f, -0.35f, lakeZ + 2.6f) { Name = "Rock", Color = new Color4(0.45f, 0.45f, 0.48f, 1f) };
    rock.Shininess = 8f;
    rock.SetScale(1.1f, 0.8f, 1f);
    scene.Add(rock);

    // --- key callbacks: set on any object (null = ignore), called automatically for every key ----------------
    // Each callback gets the key, the world point under the mouse, the camera position and the object's position.
    // F teleports the player to the mouse cursor.
    player.OnKeyDown = (key, mouse, camera, position, self, dt) =>
    {
        if (key == Keys.F) self.SetPosition(mouse.X, position.Y, mouse.Z);
    };

    // Keys B and V toggle effects by name; the depth of field keeps the player sharp.
    var post = window.Renderer.Post;
    post.Get<DepthOfFieldEffect>()!.FocusTarget = player;
    ground.OnKeyDown = (key, mouse, camera, position, self, dt) =>
    {
        if (key == Keys.B) post.Set("Bloom", "Enabled", !(bool)post.Get("Bloom", "Enabled")!);
        if (key == Keys.V) post.Set("Vignette", "Enabled", !(bool)post.Get("Vignette", "Enabled")!);
    };
}

// ---- The demo UI: a HUD and a menu, built in code (the editor's UI tab builds the same things visually) -----------
static void BuildDemoUi(Window window)
{
    var ui = window.GameUi;

    var hud = ui.Add(new UiElement(UiKind.Panel) { Name = "HUD", Offset = new Vector2(20f, 20f), Size = new Vector2(280f, 84f) });
    ui.Add(new UiElement(UiKind.Label) { Name = "Title", Text = "GT67 demo", Offset = new Vector2(16f, 10f), Size = new Vector2(240f, 24f), FontSize = 16f, Bold = true }, hud);
    var fps = ui.Add(new UiElement(UiKind.Label) { Name = "Fps", Text = "FPS", Offset = new Vector2(16f, 36f), Size = new Vector2(240f, 20f), FontSize = 13f, TextColor = new Color4(0.7f, 0.75f, 0.85f, 1f) }, hud);
    ui.Add(new UiElement(UiKind.ProgressBar) { Name = "Health", Value = 0.8f, Offset = new Vector2(16f, 58f), Size = new Vector2(248f, 12f), Fill = new Color4(0.25f, 0.8f, 0.45f, 1f) }, hud);

    ui.Add(new UiElement(UiKind.Button) { Name = "MenuButton", Text = "Menu", Anchor = UiAnchor.TopRight, Offset = new Vector2(-20f, 20f), Size = new Vector2(110f, 40f), Action = "toggle:PauseMenu" });

    var menu = ui.Add(new UiElement(UiKind.Panel) { Name = "PauseMenu", Anchor = UiAnchor.Center, Size = new Vector2(340f, 250f), Visible = false });
    ui.Add(new UiElement(UiKind.Label) { Name = "MenuTitle", Text = "Paused", Anchor = UiAnchor.Top, Offset = new Vector2(0f, 20f), Size = new Vector2(300f, 36f), FontSize = 26f, Bold = true, Align = GT67.Gui.TextAlign.Center }, menu);
    ui.Add(new UiElement(UiKind.Button) { Name = "Resume", Text = "Resume", Anchor = UiAnchor.Center, Offset = new Vector2(0f, -10f), Size = new Vector2(240f, 46f), Action = "hide:PauseMenu" }, menu);
    ui.Add(new UiElement(UiKind.Button) { Name = "Quit", Text = "Quit", Anchor = UiAnchor.Center, Offset = new Vector2(0f, 50f), Size = new Vector2(240f, 46f), Background = new Color4(0.85f, 0.3f, 0.3f, 1f), Action = "quit" }, menu);

    // Scripts and game code change elements by name, every frame if they like.
    window.OnFrameUpdate = dt => fps.Text = $"{1f / MathF.Max(dt, 0.0001f):0} FPS   (F2: debug panels)";
}
