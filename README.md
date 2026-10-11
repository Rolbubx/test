# GT67 — small OpenGL engine boilerplate (C# / .NET 8)

OpenTK 4 (OpenGL 4.1 core, macOS-compatible), Silk.NET.Assimp for models, StbImageSharp for textures,
SkiaSharp (draws the UI with the bundled JetBrains Mono font; Inter is bundled too, see `UiFonts.Family`), and Roslyn (`Microsoft.CodeAnalysis.CSharp`) for the editor's live
script compiling. No other dependencies.

## Run

```bash
dotnet run --project GT67
dotnet run --project GT67 -- --frames 300     # closes itself after 300 frames (smoke test)
dotnet run --project GT67 -- --game           # game mode instead of the editor (see IsEditor in Program.cs)
dotnet run --project GT67 -c Editor           # build configuration "Editor": opens the editor (debug build)
dotnet run --project GT67 -c Game             # build configuration "Game": runs the compiled game (optimised, no editor)
GT67/publish-game.ps1                         # the shippable game in GT67/Build: Native AOT exe (needs the VS C++ build tools)
GT67/publish-game.ps1 -NoAot                  # same without the C++ tools: trimmed ReadyToRun single-file exe
dotnet run --project GT67 -- --debug          # turns on the temporary DebugFlag.On (game mode)
```

In Rider, the run menu has **GT67 Editor** and **GT67 Game** (shared run configurations in `.run/`): they run the
same build with `--editor` or `--game`, so switching between them needs no recompile. The build configurations are
also in the solution, so Rider / Visual Studio list **Editor** and **Game** next to Debug and Release. They define `EDITOR` / `GAME` for `#if` blocks; `--editor` / `--game` still override them. `DebugFlag.On`
(`GT67.Utils`) is a temporary switch for testing: the editor toggles it in its settings menu (gear, "debug_flag", saved
in `editor.settings.json`).

Requires the .NET 8 SDK. (If only a newer runtime is installed, `RollForward=Major` lets the app start on it.)

| Key / mouse | Action |
|---|---|
| WASD, Q/E (or Ctrl/Space), Shift | move, down/up, sprint |
| Right mouse (hold) or **F1** | mouse look / toggle mouse capture |
| Scroll | zoom (FOV) |
| Left click | select the object under the cursor (editor, or with the debug window open) |
| **F2** | show/hide the debug window (hidden by default in the game) |
| **F5** | hot-reload shaders |
| Esc | quit (in the editor: deselect) |

## Project structure

```
GT67/
  Program.cs            IsEditor flag (or the Editor/Game build configuration), post-processing set-up, demo scene
  Core/
    Window.cs           GameWindow: loop, input, resize, picking
    Renderer.cs         frame: shadow pass -> opaque -> skybox -> transparent -> MSAA resolve -> post
    GameObject.cs       the main user-facing class        Scene.cs     objects, lights, raycast
    Transform.cs        TRS + parent                       Material.cs  colour/opacity/textures
    Model.cs            mesh+material parts (ref-counted)  ModelLoader.cs  Assimp import
    ModelCache.cs       the only static: path -> Model/Texture cache (Clear() it)
    Mesh.cs Vertex.cs   VAO/VBO/EBO, pos+normal+uv+tangent(+colour)
    Shader.cs Texture.cs Cubemap.cs Framebuffer.cs ShadowMap.cs PostProcess.cs Skybox.cs PhysicalSky.cs
    Camera.cs Light.cs Sun.cs PointLight.cs SpotLight.cs
    CloudLayer.cs CloudNoise.cs Texture3D.cs           volumetric clouds (rendered by Shaders/sky.frag)
    Water.cs WaterNoise.cs                             water surface (Shaders/water.vert, water.frag)
    KeyCallback.cs KeyAction.cs                        per-object keyboard callbacks
    ParameterAccess.cs                                 name-based get/set of effect settings
    PostEffect.cs SinglePassEffect.cs PostContext.cs   post-process effect base classes
    Terrain.cs TerrainBrush.cs                         editable heightfield terrain
    CollisionMode.cs Collision.cs TriangleBvh.cs MoveResult.cs   collisions (MoveAndSlide)
    Aabb.cs BoundingSphere.cs Ray.cs GLDebugger.cs
  Effects/              BloomEffect, DepthOfFieldEffect, SunRaysEffect, VignetteEffect,
                        ChromaticAberrationEffect, FilmGrainEffect, ColorGradingEffect, ColorCorrectionEffect
  Scripting/            Script (base class), Game (the API for scripts), ScriptCompiler (Roslyn)
  Editor/               MapEditor (+ Layout, UiDesigner, Terrain), TransformGizmo, LightMarkers, BrushRing,
                        ModelImporter, ScriptWorkspace, ExternalEditor, EditorSettings, MapGenerator
  Scripts/              your C# scripts (Spin, PlayerController, GameCamera, _Usings.cs)
  Generated/            GeneratedMap.cs, written by the editor's Compile button
  Gui/                  the UI toolkit (SkiaSharp, immediate mode): Gui (+ Widgets, Controls, InputText, Overlay,
                        Runtime), Theme, UiFonts, UiSurface, UiHost, GuiInput, GuiProps, Icons
  UI/
    DebugUI.cs          stats, camera, scene, inspector, lighting, water, render panels
    ParameterEditor.cs  widgets for any object's public settings
    UiCanvas.cs UiElement.cs   the game's own UI (HUD, menus), designed in the editor
  Utils/
    Primitives.cs       Cube, Plane, Sphere, Cylinder, Cone, Capsule, Torus, CameraBody, Heightmap, Quad, Grid, Line, Axes
    Assets.cs           LoadModel(path) helper (imported globally in GlobalUsings.cs)
    Log.cs MathUtil.cs
  Shaders/              lit (Blinn-Phong + shadows), sky, depth, post
  Assets/               Models/  Textures/  Skybox/  (copied next to the executable)
```

## The map editor and the generated map

Set `bool IsEditor = true;` at the top of `Program.cs` (or build with the **Editor** configuration, or start with
`--editor`; `--game` forces game mode) to open the editor. The editor and the game both start from the demo scene at
the bottom of `Program.cs` (with its HUD and menu); `--generated` builds the game the editor wrote instead.

**Workflow:** build the map in the editor, press **Play** to try it (**Stop** restores the scene), press **Compile**,
restart (`dotnet run`). Compile (1) compiles `Scripts/*.cs` and reports errors, and (2) writes the whole scene to
`Generated/GeneratedMap.cs`, one big C# file that rebuilds it: terrain heights and paint, every object with its model,
material, texture, transform, collision mode and attached scripts, water, sun, sky, clouds and lights. Models and
textures from anywhere on your disk are copied into `Assets/` so the project is self-contained. The file compiles with
the project, so the game finds it at startup (`GeneratedMap.IsGenerated`) and the editor also starts from it. To go back
to the demo, put back the placeholder `GeneratedMap.cs` (`IsGenerated = false`).

| In the editor | |
|---|---|
| **Fly** | hold the right mouse button and use WASD / QE, Shift to sprint, scroll to zoom |
| **Import models** | drop `.fbx` `.gltf` `.glb` `.obj` `.dae` files from your file manager onto the window; they load from where they are and are copied into `Assets/Models/` on Compile |
| **Add things** | the **+** button of the **Hierarchy** (shapes, point light, water, player, camera, sound), or drag a tile from the **Project** panel into the viewport (it lands where the mouse points) |
| **Select** | click an object or light in the viewport, or pick anything in the **Hierarchy**: objects, lights, water, sounds, the scene's **Volume** (post-processing) and **Environment** (sun, sky, rain, fog). The **Inspector** edits whatever is selected |
| **Move / Rotate / Scale** | tools **W / E / R** (Q = select). **Drag an object itself** to slide it over the ground (Shift + drag = up/down; works with Q and W). Arrows move along one axis, rings rotate, cube handles scale per axis and the centre cube scales uniformly. The toolbar switches World/Local axes and turns on **Snap** (moves land on the grid; steps in the gear menu) |
| **Player and camera** | **Hierarchy > + > Player** adds a capsule tagged `Player` that walks with WASD/arrows (Shift runs, Space jumps), plus a **Camera** that follows it; **Camera** adds one on its own. Pick the camera's **Mode** in the Inspector: ThirdPerson, FirstPerson, TopDown or Fixed |
| **Player view** | **Play** looks through the game camera; **Free view** in the toolbar flies around while the game runs, **Player view** goes back. Stop puts your editor camera back |
| **Lighting** | select **Environment** in the Hierarchy for the sun (time of day, latitude, season), ambient, physical sky, clouds, rain and fog; every point/spot light is in the Hierarchy too, with a marker in the viewport you can pick and move |
| **Render** | the scene's **Volume** (Hierarchy) holds tone mapping, exposure and every post-processing effect; the **Render** tab next to the Inspector has shadows, skybox, VSync and quality |
| **Delete / Duplicate / Focus** | `Del` / `Ctrl+D` / `F` (or double-click in the Hierarchy) |
| **Scripts** | **Project > Scripts** (bottom panel) creates scripts and opens them in your own editor (below); drag a script onto an object, in the viewport or the Hierarchy, to attach it |
| **Scenes** | the scene button at the top of the Hierarchy opens, adds, renames and deletes scenes (below) |
| **Game UI** | the **UI** tab next to the Hierarchy designs the game's HUD and menus (below) |
| **Terrain** | the Terrain tool (below) |

Like Unity, the **Hierarchy** is on the left, the **Inspector** on the right, and the **Project** browser (shapes, models,
prefabs, scripts, shaders, sounds), the **Console** and the **API** reference along the bottom; drag the thin bars between
them to resize.

### Scenes

A game has one or more scenes, each with its own environment, objects, sounds and **Volume** (post-processing). The scene
button at the top of the Hierarchy switches between them, adds new ones, renames them and picks the one the game starts
with. Compile writes every scene (`Generated/GeneratedGame/Scenes/<Name>/`). Scripts switch scenes like Unity's
SceneManager; the new scene starts fresh at the end of the frame:

```csharp
Game.LoadScene("Level 2");     // Game.ReloadScene() starts the current one again
Game.Log(Game.SceneName);      // Game.SceneNames lists them all
```

`dotnet run -c Game -- --scene "Level 2"` starts the compiled game in another scene, to test it.

### Editing scripts in Visual Studio Code

The editor has no code editor of its own. **Open project in editor** and double-clicking a script open it in Visual Studio
Code (`code`, which must be on the PATH, as the VS Code installer does by default). Save the file there and the editor
recompiles the scripts automatically and swaps the new code onto every object (turn this off in the gear menu). Change
the program in the gear menu: `rider`, `notepad`, ... or `default` for the system's own choice for `.cs` files; if the
program is not found the system default is used. Settings are kept in `editor.settings.json` next to the project file.
The check for saved files runs while the editor window is drawn, so it recompiles as soon as you switch back to it.

### Designing the game's UI

Open the **UI** tab next to the Hierarchy. Add a Panel, Label, Button, Progress bar, Slider, Check box, Text box or Image (or a
ready-made HUD / Menu preset), click it in the viewport to select it, drag it to move it and drag its corner to resize it
(`Snap` rounds to 8 px). Elements nest inside panels and are placed from an **anchor** of their parent (corners, edges,
centre or stretch) plus an offset, so they stay in place at any window size; *UI scale* enlarges everything. The properties
tab sets text, font size, colours, border, corners, value range, image and what a button does. Drop an image file on the
window to use it (it is copied to `Assets/UI` on Compile).

A button's **Action** is `quit`, `show:Name`, `hide:Name`, `toggle:Name`, `cursor:on`, `cursor:off`, or any other text,
which reaches your code. Play shows the UI working; Compile writes it into `GeneratedMap.cs`, so the game builds it at
start. Scripts and `Program.cs` reach it by name:

```csharp
Game.Ui.Find("HealthBar").Value = 0.4f;               // progress bar / slider value
Game.Ui.Find("Score").Text = $"Score {score}";        // label text
Game.Ui.Find("PauseMenu").Visible = true;
Game.Ui.ActionTriggered += e => { if (e.Action == "start") { /* ... */ } };   // custom button actions
Game.Ui.Find("Volume").Changed += e => volume = e.Value;                      // slider / checkbox / text box changes
```

`Program.cs` builds a small HUD and menu in code (`BuildDemoUi`) for the demo scene.

Not saved in the map: C# lambdas set in code (`OnUpdate`, `OnKeyDown`...). Each scene's Volume (post-processing settings) is saved; the effects themselves are added in
`Program.cs`. Everything else in the scene is written, including objects you created in code.

## Importing models (FBX, glTF, OBJ...) and collisions

`LoadModel("Assets/Models/thing.fbx")`, `Model.Load(path)` and the editor all use the same importer (Assimp):

- Textures are found wherever the model keeps them: next to it, in sub-folders, under the **absolute path of the
  artist's machine** (it falls back to searching for the file name near the model), or **embedded in the file**
  (`.glb`, many FBX). Diffuse/base-colour, normal (or bump) and specular maps are used, plus material colour and opacity.
  glTF metallic-roughness maps are not (the shading is Blinn-Phong).
- The editor sizes an import: a model that looks like centimetres (larger than 40 units) is scaled by 0.01. Animations and
  skeletons are not imported.
- Tested with glTF (embedded and external textures), OBJ + MTL and an ASCII FBX; binary FBX takes the same Assimp path.

Every object is solid by default. `obj.Collision` is `None`, `Box`, `Sphere` or `Mesh` (exact triangles, accelerated by a
BVH that is built when a model is imported, so even 100k-triangle models cost about 2 ms per query).

```csharp
// A player: gravity + sliding along walls and over terrain and imported models.
var result = self.MoveAndSlide(velocity * dt, radius: 0.5f);        // moves the object like a sphere
if (result.Grounded && Game.WasKeyPressed(Keys.Space)) velocity.Y = 6f;
scene.MoveAndSlide(position, delta, radius, ignore: self);          // the same without an object
scene.OverlapSphere(center, radius);                                // which solid objects are near
obj.OnCollision = (self, other, normal) => { ... };                 // and scripts get OnCollision(other, normal)
```

## Scripts

A script is a C# class in `Scripts/` that inherits `Script`; attach it to any object in the Inspector or with
`obj.AddScript(new Spin { DegreesPerSecond = 90f })`.

```csharp
public class Spin : Script
{
    public float DegreesPerSecond = 45f;                  // public fields/properties of simple types show in the editor
    public override void OnUpdate(float deltaTime) => Self.RotateY(DegreesPerSecond * deltaTime);
}
```

Hooks: `OnStart`, `OnUpdate(dt)`, `OnLateUpdate(dt)` (after every object updated: cameras), `OnKeyDown/OnKeyHeld/OnKeyUp(key, mouseWorld, cameraPosition)`,
`OnCollision(other, normal)`, `OnDestroy`. Properties: `Self` (the object), `Scene`, `Game`, `Enabled`.
`Game` is the small API (the editor's Code > API tab lists all of it, with descriptions):

- **Time**: `Time`, `DeltaTime`, `UnscaledDeltaTime`, `TimeScale` (0.5 = slow motion), `Paused`.
- **Input**: `IsKeyDown`, `WasKeyPressed`, `WasKeyReleased`, `IsMouseDown`, `WasMousePressed`, `WasMouseReleased`,
  `MousePosition`, `MouseDelta`, `MouseScroll`, `IsMouseOverUi`, `MouseWorldPosition`, `MouseHit`, `CursorLocked`,
  `FreeCamera` (false = WASD no longer flies the camera).
- **Objects**: `Find(name)`, `FindByTag(tag)`, `Spawn("cube", position, color)`, `SpawnModel(file, position)`,
  `Instantiate(original, position)`, `Destroy(obj)`, `Destroy(obj, seconds)`.
- **Queries**: `Raycast(origin, direction, maxDistance)` (a `RaycastHit` with `Object`, `Point`, `Distance`),
  `OverlapSphere(center, radius)`, `GroundHeight(x, z)`.
- **Timers**: `After(seconds, action)`, `Every(seconds, action)`; both return a `GameTimer` you can `Cancel()` or pause.
- **World**: `StartRain`, `StopRain`, `Rain`, `Sun`, `SetTimeOfDay`, `Fog`, `AddLight(position, color, intensity, range)`,
  `RemoveLight`, `PlaySound`, `FindSound`, `Ui`, `Camera`, `Scene`.
- **Other**: `Random(min, max)`, `Chance(p)`, `RandomPointAround(center, radius)`, `Log(text)`.

```csharp
public class Spawner : Script
{
    public float Interval = 2f;
    public override void OnStart() => Game.Every(Interval, () =>
    {
        var ball = Game.Spawn("sphere", Game.RandomPointAround(Self.Position, 5f) + Vector3.UnitY * 8f, Color4.Orange);
        Game.Destroy(ball, 10f);
    });

    public override void OnUpdate(float dt)
    {
        if (Game.WasMousePressed(MouseButton.Left) && !Game.IsMouseOverUi && Game.MouseHit is { } hit)
            Game.Log($"Clicked {hit.Object.Name} at {hit.Point}");
        if (Game.WasKeyPressed(Keys.P)) Game.Paused = !Game.Paused;
    }
}
```

When the editor stops playing, timers are cancelled and `TimeScale`, `FreeCamera`, the cursor and the lights added
with `AddLight` are put back.

`Scripts/_Usings.cs` gives every script the engine, scripting and OpenTK maths namespaces and `Keys`. Two examples are
included (`Spin`, `PlayerController`, `GameCamera`). In the editor scripts are compiled live with **Roslyn**
(`Microsoft.CodeAnalysis.CSharp`; only the editor uses it) when you press Compile or save a script file, and the new version replaces the old one on every object, keeping its settings. The game build compiles the
same files normally. Scripts only run while the editor is playing (always in the game). A script that throws is
disabled and the error is logged.

## Custom shaders (the GT67 shader language)

A `.shader` file in `Shaders/Custom` describes how a surface looks, and optionally how its vertices move; the engine
builds a full lit shader from it, so lighting, shadows, fog, rain, puddles and caustics keep working. Pick it in the
Inspector (Shader), or `obj.SetShader("Name")`. Files reload by themselves when saved; errors show in the Inspector
with their line numbers. Shaders > New shader starts from a commented template.

```glsl
param color Tint = (1.0, 0.4, 0.1)
param float Speed = 2.0 range 0 10              // a slider in the Inspector
param bool Animate = true                       // a checkbox
param vec2 Scroll = (0.1, 0.0)
param texture Mask = "Assets/Textures/mask.png" // white when empty or missing

vertex                                          // optional: move the vertices (world space)
{
    position += normal * sin(time * Speed + position.y * 4.0) * 0.03;
}

surface
{
    float n = fbm(position * 3.0, 4);
    albedo = base * Tint * (0.8 + 0.4 * n);
    emission = Tint * texture(Mask, uv + Scroll * time).r * (Animate ? 0.5 + 0.5 * sin(time * Speed) : 1.0);
    normal = bump(normal, position, n, 0.3);
    alpha = n + 0.2;                            // below 0.5 the pixel is cut away
}
```

| | |
|---|---|
| **params** | `float` (with `range min max` for a slider), `color`, `vec2`, `vec3`, `bool`, `texture "path"`; per-object values in the Inspector, from code `obj.SetShaderParam("Speed", 3f)`, `obj.SetShaderTexture("Mask", path)` |
| **surface reads** | `uv`, `position` (world), `localPosition` (model), `objectPosition`, `view`, `time`, `base` (colour and texture), `vertexColor`, `screenUV`, `sunDirection`, `sunColor`, `wetness` |
| **surface sets** | `albedo`, `emission`, `shininess`, `specular`, `normal` (bumps), `alpha` (cut-out below 0.5) |
| **vertex** | changes `position`, `normal`, `uv`; reads `localPosition`, `objectPosition`, `time`, `vertexColor` (shadows and reflections keep the unmoved shape) |
| **library** | `noise(p)`, `fbm(p, octaves)`, `voronoi(p)` (distance, cell id, edge), `hash(p)`, `hash2(p)`, `remap`, `saturate`, `rotate(p, angle)`, `hsv(c)`, `rgbToHsv(c)`, `fresnel(normal, view, power)`, `stripes`, `checker`, `circle`, `triplanar(tex, position, normal, scale)`, `bump(normal, position, height, strength)` (surface only) |

Examples in `Shaders/Custom`: **Pulse** (glow), **Dissolve** (noise + cut-out + burning edge), **Sway** (wind in grass,
plants and flags, a vertex block), **Hologram** (fresnel rim, screen-space scanlines, a texture param). Compile ships the
`.shader` files and their textures with the game.

### Screen shaders (the whole picture: PS1, CRT...)

A `.shader` file with a `screen` block changes the whole picture instead of one object. Add it to a scene's **Volume**
(Hierarchy > Volume > *Add screen shader*, or drag its tile from Project > Shaders into the view); each scene has its own
list, run in order after the built-in effects, with the `param` settings in the Inspector. **New screen shader** in
Project > Shaders starts one from a template.

```glsl
// Shaders/Custom/Posterize.shader
param float Levels = 6.0 range 2 32
// stage hdr             // optional: run before tone mapping, on linear light (the default runs after it)
// vertexSnap 160        // optional: vertices snap to a 160-row screen grid while it is on (the PS1 wobble)

screen
{
    color = floor(color * Levels + 0.5) / Levels;
}
```

The block reads and changes `color` (this pixel), reads `uv` (0..1), `resolution` (pixels), `time`, `depth` (metres from
the camera to what is seen there), `worldPosition` (where that is in the world: `worldPosition.y` is its height) and
`cameraPosition`, and can call `sampleScene(uv)`, `sampleDepth(uv)`, `sampleWorld(uv)`, `isSky(uv)`, `dither(pixel)`
(4x4 Bayer, -0.5..0.5) and the library above. For example `if (!isSky(uv) && worldPosition.y < 20.0) color *= 0.5;`
darkens everything below 20 m. Bundled: **PS1** (240p big pixels, 15-bit colour with dithering, vertex wobble, optional short fog) and
**CRT** (curved glass, scanlines, RGB mask, colour bleed). From a script:

```csharp
Game.Volume.AddScreenShader("PS1");
Game.Volume.GetScreenShader("CRT")?.Set("Curvature", 0.2f);
```

Screen shaders also read `sunDirection` (towards the sun) and `sunColor` (its light, zero when it is off).

### Sky shaders (your own sky and sun)

A `.shader` file with a `sky` block draws the sky. Pick it in Hierarchy > **Environment** > Sky > *Shader* (or drag its
tile into the view); **New sky shader** in Project > Shaders starts one. `color` starts as the engine's sky in that
direction (the physical atmosphere with its clouds, or the gradient): keep it, change it or replace it. It reads
`direction`, `sunDirection`, `sunColor`, `sunSize` (radians), `time` and `cameraPosition`, and can call
`sunDisc(direction, sunDirection, size)`, `skyColor(direction)` and the library. Colours are linear light (the sun is far
brighter than 1). Storm and fog still cover it. Bundled: **ToonSky** (day / sunset / night gradient that follows the sun,
a hard toon sun with a glow ring, a moon and stars).

```glsl
sky
{
    vec3 mine = mix(vec3(0.9, 0.5, 0.3), vec3(0.1, 0.2, 0.6), saturate(direction.y));
    mine += sunColor * sunDisc(direction, sunDirection, 0.05) * 10.0;   // a big sun
    color = mine;
}
```

In **Environment**, the **Sun** can be switched off (light and disc), keep its light but hide the disc, or be removed and
added again; the **Physical sky** can be switched off, which draws the Top / Horizon / Bottom gradient instead (with the
sun's disc) and leaves the ambient light to you.

## Prefabs

A prefab is a reusable object saved to `Prefabs/<Name>.prefab` (JSON): the object and its children, with their mesh,
material, texture, custom shader, tag, collision and scripts (with their settings).

- **Make one**: select an object, type a name in the Inspector's **Prefab** section and press **Save as prefab**.
  Models and textures from outside the project are copied into `Assets/` so the game finds them (a texture generated
  in code is left out, with a warning).
- **Place copies**: **Assets > Prefabs**, click (in front of the camera) or drag into the viewport.
- **Edit all copies**: change one copy, then **Apply** (saves it into the prefab and rebuilds every other copy, which
  keep their own name, place and rotation). **Revert** throws away a copy's changes, **Unlink** detaches it.
- **From code**: `Game.SpawnPrefab("Enemy", position)` in a script, or `Prefab.Spawn(scene, "Enemy", position)`.

`Prefabs/Player.prefab` is an example: the walking capsule with `PlayerController`. The build copies `Prefabs/` next
to the executable, and copies keep their link (`GameObject.PrefabName`) through Compile.

## Performance

- **Frustum culling**: objects outside the camera's view are not drawn; water and puddle reflections cull against
  their own mirrored view, and the shadow pass skips casters outside the sun's shadow box.
- **Occlusion culling**: after the visible objects are drawn, the bounding box of each object with at least
  `renderer.Occlusion.MinTriangles` (200) triangles is tested against the depth buffer with a GPU query; an object
  whose box is fully hidden is skipped until a later query sees it again. Answers are read only when ready, so the CPU
  never waits; an object coming out from behind a wall can appear one frame late.
- Opaque objects are drawn front to back, so hidden pixels are rejected before shading.
- `renderer.FrustumCulling` / `renderer.OcclusionCulling` switch them off; the debug panel's Performance section shows
  how many objects were drawn, off screen, hidden and skipped for shadows.

**Model import**: textures are decoded on all CPU cores while the meshes are built, collision is prepared in the
background, and meshes sharing a material are merged (fewer draw calls). The log shows each model's load time.
glTF/GLB textures come along automatically: embedded images (`*0`, or by their file name), external files (also with
spaces, written `%20`) and the base colour slot. A model dropped onto the editor from outside the project is copied,
with its textures and side files, into `Assets/Models/<name>/`.

## Terrain

```csharp
var terrain = new Terrain(width: 80f, depth: 80f, resolution: 160);   // a flat heightfield object
scene.Add(terrain);
terrain.Sculpt(TerrainBrush.Raise, worldPoint, radius: 6f, strength: 4f, deltaTime);   // Raise, Lower, Smooth, Flatten, Noise
terrain.Paint(worldPoint, 6f, Color4.Orange, strength: 3f, deltaTime);                  // vertex colours
terrain.Apply();                                                                        // update mesh + normals
terrain.FinishEdit();                                                                   // rebuild collision (end of a stroke)
float y = terrain.GetHeight(x, z);   bool hit = terrain.Raycast(ray, out var point);
```

In the editor choose the **Terrain** tool and hold the left mouse button over the ground: pick Raise, Lower (or Shift
to invert), Smooth, Flatten (levels to where you clicked), Noise or Paint, with a radius, strength and colour palette.
The terrain is a normal object with mesh collision; `GeneratedMap` stores its heights and colours as base64. Do not
rotate or scale it. It is coloured per vertex (no texture layers yet).

## Grass

```csharp
scene.Grass = new GrassLayer { Height = 0.45f, WindStrength = 0.35f };
scene.Grass.Scatter(new Vector2(-20, -20), new Vector2(20, 20), density: 40f,
    (x, z) => terrain.GetHeight(x, z));          // return null where no grass should grow
```

Thousands of blades drawn with GPU instancing (one draw call per 16 m chunk on screen; the demo has about 62,000).
Every blade has its own height, width, facing, lean and shade, and the field has noise patches (lush, tall and dark
in places, shorter and drier in others) and a few wildflowers. Blades turn partly towards the camera so they never
vanish edge-on, wind gusts roll across the field as visible waves of lighter grass, bend away from objects tagged
`Player` walking through them, get the sun's shadows, glow when the sun is behind them, darken when wet, and thin out
gradually towards `MaxDistance`. Settings: `Height`, `Width`, `BaseColor`, `TipColor`, `WindStrength`, `WindSpeed`,
`WindDirection`, `MaxDistance`, `PlayerBendRadius`, `Flowers` (share of blades that are flowers, 0.015). Brushes from code: `Paint(center, radius, density, ground, random)`,
`Erase(center, radius, fraction, random)`, `Reground(...)`.

**In the editor**: Terrain tab > Grass > **Paint grass** (or the Grass brush of the Terrain tool): left-drag on any ground,
terrain or not, to grow grass up to the chosen density; Shift removes it. It never grows under water and follows the
terrain when you sculpt. Compile saves every blade with the map.

## Object API

```csharp
var tree   = new GameObject(LoadModel("Assets/Models/tree.obj"), 0, 0, 0);   // cached by path
var crate  = new GameObject(Primitives.Cube(), 2, 0.5f, -3) { Name = "Crate", Color = Color4.Orange };
var ground = new GameObject(Primitives.Plane(50, 50), 0, 0, 0);
scene.Add(tree, crate, ground);

crate.SetTexture("Assets/Textures/crate.png");
crate.OnUpdate = (self, dt) => self.RotateY(40f * dt);       // behaviour without subclassing
crate.AddChild(new GameObject(Primitives.Sphere(0.2f), 0, 1.1f, 0));

crate.LookAt(tree);
float d = crate.DistanceTo(tree);
bool hit = crate.Intersects(tree);                            // AABB test
var clone = crate.Clone();                                    // shares the model
crate.Destroy();                                              // leaves the scene, releases the model

GameObject? picked = scene.Raycast(origin, direction);        // closest hit, triangle accurate
scene.Find("Crate"); scene.FindByTag("enemy");
```

Properties: `Name Id Tag Enabled Visible Parent Children`, `Position X Y Z`, `Rotation` (quaternion),
`EulerAngles` (degrees), `Scale` / `UniformScale`, `Color Opacity Shininess Unlit Material`,
`CastShadow ReceiveShadow Wireframe CullFace`, `Bounds` (world AABB), `BoundingSphere`, `OnUpdate`,
`OnKeyDown OnKeyHeld OnKeyUp` (see below).
Methods: `SetPosition Translate Rotate RotateX/Y/Z SetScale LookAt AddChild RemoveChild Clone DistanceTo
Intersects SetTexture SetMaterial SetColor Destroy Update Draw`.

### Key callbacks

Any object can react to the keyboard. The callbacks are properties that can be null (the default); when set they are
called **automatically** for every key, on every enabled object that has one, with no selection needed.

```csharp
// (key, mouse position in the world, camera position, this object's position, the object, delta time)
player.OnKeyHeld = (key, mouse, camera, position, self, dt) =>      // every frame while a key is down
{
    var forward = position - camera; forward.Y = 0; forward.Normalize();
    if (key == Keys.Up) self.Translate(forward * 4f * dt);          // walk away from the camera
};
player.OnKeyDown = (key, mouse, camera, position, self, dt) =>      // once, when a key goes down
{
    if (key == Keys.F) self.SetPosition(mouse.X, position.Y, mouse.Z);   // teleport to the mouse cursor
};
player.OnKeyUp = null;                                              // when a key is released
```

- `mouse` is the world point under the cursor: where the cursor ray hits the scene, else where it crosses the ground
  plane (`y = 0`), else 100 units along the ray. While the mouse is captured (F1) it is the screen centre.
- `self` is passed so one lambda can be shared by several objects; `Clone()` copies the callbacks.
- They are skipped while a UI text box has focus. WASD/QE also fly the camera: set `window.FreeCamera = false`
  if your objects use those keys.

Conventions: right-handed, Y up, an object's forward is local **-Z**. Colours are given in sRGB and converted to
linear for lighting; the post pass applies tone mapping + gamma (turning it off shows raw linear output, which
looks dark).

Ownership: a `GameObject(Mesh)` owns that mesh (wrapped in its own model); don't pass the same `Mesh` to two
objects — build a `Model.FromMesh(mesh)` and share the model, or use `Clone()`.

## How to…

**Add a primitive** — add a static method to `Utils/Primitives.cs` that builds `Vertex[]` + `uint[]` and returns
`new Mesh(vertices, indices, generateTangents: true)` (use `PrimitiveType.Lines` for line meshes, with
`Unlit = true` on the object).

**Load a model** — drop the file (obj, fbx, gltf, …) and its textures into `Assets/Models/`, then
`new GameObject(LoadModel("Assets/Models/thing.obj"))`. `Model.TryLoad(path)` returns null with a warning instead
of throwing. Diffuse/specular/normal (or OBJ "bump") maps and the diffuse colour are picked up from the material.

**Add a light**

```csharp
scene.AddLight(new PointLight(new Vector3(0, 2, 0), Color4.Red, intensity: 4f, range: 10f)); // max 8
scene.Spot = new SpotLight(position, direction) { InnerAngle = 15, OuterAngle = 25 };
scene.Sun  = new Sun { TimeOfDay = 16f };                                                    // see "The sun" below
```

**Add a debug panel** — add a `DrawMyPanel()` method to `UI/DebugUI.cs` using `Gui` widgets (`g.Slider("Label", value, min, max, v => ...)`, `g.Button(...)`), and call it
from `Draw`. It has access to `_window.Scene`, `_window.Camera` and `_window.Renderer`.

**Skybox** — put `right/left/top/bottom/front/back` images (png/jpg/bmp/tga) in `Assets/Skybox/`. Without them the
demo uses the physically based sky (below).

## The sun

`scene.Sun` is a `Sun`, not a generic light: it has a place in the sky and drives the scene's shadows.

```csharp
scene.Sun = new Sun { TimeOfDay = 16f, TimeSpeed = 0.5f, Latitude = -30f, Declination = 23f, NorthRotation = -40f };
scene.Sun.Elevation; scene.Sun.Azimuth;            // degrees above the horizon / compass bearing (read-only)
scene.Sun.Direction = new Vector3(-1, -2, -1);     // or aim it by hand (switches AutoPosition off)
```

- **Position**: `TimeOfDay` (hours), `TimeSpeed` (in-game hours per real second), `Latitude`, `Declination`
  (season), `NorthRotation` (turns the compass). Axes: +X east, -Z north. `AutoPosition` false = use `Direction`.
- **Light**: `Color`, `Intensity`, `Enabled`; `AtmosphericLighting` tints and dims it by the air it crosses (orange and
  weak at sunset, off at night, only with a `PhysicalSky`). `LightColor` is what the renderer actually uses.
- **Disc** (in the physical sky): `AngularRadius`, `DiscBrightness`.
- **Shadows**: `CastShadow`, `ShadowSize`, `ShadowCenter`, `ShadowBias`.

## Physically based sky

`PhysicalSky` is a single-scattering atmosphere (Rayleigh + Mie, ray-marched in `Shaders/sky.frag`) lit by `scene.Sun`.
Give it to a `Skybox` instead of a cubemap:

```csharp
scene.Skybox = new Skybox(new PhysicalSky { MieCoefficient = 0.03f });
```

- It describes the air only: `Brightness`, `MieCoefficient` + `MieAnisotropy` (haze), `RayleighCoefficient` (air
  colour), `PlanetRadius`/`AtmosphereHeight`, `GroundAlbedo`, `Stars`, `AmbientScale`, `NightAmbient`.
- With the sun's `AtmosphericLighting` on, it also sets the scene's ambient colour/intensity from the sky, so shadows,
  sun rays and bloom follow the time of day.
- Limits: single scattering only (no multiple scattering or ozone), so twilight is darker than reality, and
  the ground below the horizon is a flat approximation. The Lights panel has live controls.

### Volumetric clouds

```csharp
scene.Skybox = new Skybox(new PhysicalSky { Clouds = new CloudLayer { Coverage = 0.55f, WindSpeed = 15f } });
```

`CloudLayer` ray-marches a slab of 3D noise (Perlin-Worley shape plus Worley erosion, generated at startup in about
0.3 s) with Beer-Lambert absorption, a silver lining towards the sun, self-shadowing, sky ambient on the shaded sides,
atmospheric haze with distance and wind drift. The sun's colour and the time of day tint the clouds automatically.

- Shape: `Coverage`, `Density`, `Altitude` and `Thickness` (metres), `ShapeSize`, `DetailTiling`, `DetailStrength`, `Seed`.
- Randomness: a seeded weather noise map makes some areas clear, others overcast, with small puffs next to tall towers
  (`Variation`, 0 = uniform, 0.6 default; `WeatherSize`, metres per weather pattern). The shapes also slowly boil and
  change over time (`Evolution`, 0 = frozen). Change `Seed` for a different sky.
- Motion: `WindDirection`, `WindSpeed`. Light: `SunIntensity`, `AmbientIntensity`, `Anisotropy`, `Tint`, `Haze`.
- Quality: `Steps` (default 32), `LightSteps` (5), `MaxDistance`. `Enabled = false` hides them.
- Cost: about 6 ms per frame on a GTX 1060 with the sky filling the whole screen, less when objects cover it.
- Limits: a flat slab (no planet curvature, so none below the horizon), no cloud shadows on the ground, and sampling
  noise is hidden only by blur and dithering (there is no temporal accumulation).

## Water

```csharp
// A lake: a heightmap ground with a basin, and water at level -0.3 over it (see Program.cs).
var ground = new GameObject(Primitives.Heightmap(50, 50, 160, (x, z) => /* height at x, z */ 0f), 0, 0, 0);
var lake = new Water(x: 9f, level: -0.3f, z: -6f, width: 14f, depth: 14f) { WaveHeight = 0.07f };
scene.Add(lake);
```

**In the editor**: Assets > World > **Water (lake)** — click it for a 16 x 16 m lake in front of the camera, or drag it
into the viewport to place it. On a terrain it digs its own basin; elsewhere it sits just above the ground. The new
water is selected: move it with **W** (the up arrow changes its level), resize it with **R** (or type Width / Depth in
the Inspector), **Dig basin** lowers the terrain under it to match a new size, **Del** removes it. Click a water surface
in the viewport (or in the Hierarchy) to select it again.

**Sea to the horizon**: tick **Reach horizon** (`ReachHorizon = true`): the water then follows the camera out to the end
of the view, and below the horizon the sky is drawn as distant sea (its deep colour turning into a mirror of the sky),
so there is no grey band or edge between the sea and the sky. Only its level is used.

**Size and detail**: `Width` and `Depth` can be changed at any time. The surface is drawn as a quadtree of tiles: the
nearest are `TileSize` metres, and they double in size at every doubling of `LodDistance`; every tile has the same
`Detail` x `Detail` grid, so a sea of several kilometres takes a few dozen draw calls. Before a tile hands over to the
larger one, its in-between vertices glide onto the larger grid (geomorphing), so the change never pops; small strips
hanging under the tile edges hide any remaining seam. Waves too small for a tile's grid are averaged away (mipmaps)
instead of turning jagged, and the lighting is per pixel, so distant tiles do not look faceted.

**Waves (FFT ocean)**: the surface is real ocean noise (Tessendorf, "Simulating Ocean Water"): a 128 x 128 patch summing
16384 waves drawn at random from an ocean spectrum (Phillips) around `WindDirection` (`DirectionSpread` sets how
tightly), each moving at the speed real water waves of its length move. Every frame the spectrum is turned into heights,
slopes and choppy sideways displacement with inverse FFTs on a background thread (about 3 ms of CPU in a Release build,
0.1 ms on the render thread). The patch is drawn twice, as is and 2.7x larger turned by 34 degrees, so it never visibly
repeats; a third, smaller copy adds the fine chop real water always has, so it glitters and breaks up reflections even
with the ripple layers off. `WaveLength` is the longest waves, `WaveHeight` a typical crest, `Choppiness` sharpens them, and foam forms
where the surface folds over (whitecaps). The small ripples on top are periodic Perlin noise with domain warping, in
layers turned at odd angles, with slow rougher and calmer patches. `EdgeFade` (metres, wider with distance) makes the
outer border of a rectangle of water fade out instead of ending in a hard straight line.

`Water` is a rectangle at a given level (it is not a `GameObject`; the scene keeps a separate `Waters` list). It draws:

- **Reflections** of the scene and the sky (clouds included): the scene is rendered mirrored in the water plane into a
  target at 3/4 of the screen resolution (`ReflectionScale`), with everything below the surface clipped away.
- **Refraction** of what lies under the surface, bent by the ripples, with colour that absorbs with depth
  (`Absorption`: red disappears first) and turns from `ShallowColor` to `DeepColor`; the depth comes from the depth buffer.
- **Waves**: FFT ocean waves moving the mesh (`WaveHeight`, `WaveLength`, `Choppiness`, `WindDirection`,
  `DirectionSpread`, `Speed`) plus scrolling ripple layers for fine detail, the finest only close to the camera (`RippleStrength`, `RippleScale`).
- A Fresnel mix between the two (`FresnelF0`), a sun glint (`SpecularStrength`, `Shininess`) with pin-point
  `Sparkle`s on the ripples, sunlight through the wave tops (`CrestLight`), foam on crests and along
  shores or around submerged objects (`FoamAmount`, `ShoreFoamWidth`) and a soft shoreline (`EdgeSoftness`).
- **Splashes and wakes**: anything that moves (an object driven by a script or `OnUpdate`) splashes when it drops in
  and leaves rings behind it as it moves through the water (`Interaction` sets the strength, 0 = none);
  `water.Splash(position, strength)` or `Game.Splash(position)` makes rings from code.
- **Caustics**: the light the waves focus onto everything under the water (lake bed, rocks, legs): a moving web of
  bright lines, strongest just under the surface (`Caustics`, 0 = none). Lapping foam bands roll in towards the shore.
- `GetHeightAt(x, z)` gives the wave height for floating objects (vertical motion only, so it is approximate);
  `Game.WaterAt(position)` finds the water under a point.

**Floating**: attach the `Buoyancy` script (Scripts/Buoyancy.cs): the object bobs on the real waves, tilts with their
slope, drifts with the wind and settles; `Density` sets how it floats (0.2 high like a cork, 0.5 half under, above 1 it
sinks), `Drag`, `Tilt` and `Drift` tune it. Out of the water it falls until it lands.

Every setting is a property, so it can also be set by name: `ParameterAccess.TrySet(lake, "WaveHeight", 0.1f)`. The
Water panel in the debug UI has live sliders. `Primitives.Heightmap(width, depth, subdivisions, height, color?)` builds
terrain from a height function (with optional vertex colours, used for the sandy lake bed).

Cost: about 1 ms per frame on a GTX 1060 for the demo lake (the reflection pass plus one scene copy).

Limits: no underwater view (the surface is hidden while the camera is below it), transparent objects are drawn after the
water so they do not show through it or in its reflection, the reflection is rendered at reduced resolution, and
objects do not make ripples or float by themselves.

## Post-processing effects

Effects are added in code (see the end of `Program.cs`) and tuned in code or by name (below). They run in the order added: **HDR**
effects first, then exposure + tone mapping + gamma, then **LDR** effects.

```csharp
var post = window.Renderer.Post;
post.Add(new DepthOfFieldEffect { FocusTarget = crate, FocusRange = 12f, MaxBlur = 6f });
post.Add(new BloomEffect { Threshold = 0.9f, Intensity = 0.5f });
post.Add(new SunRaysEffect());                 // follows scene.Sun
post.Add(new ChromaticAberrationEffect());
post.Add(new ColorGradingEffect { Saturation = 1.2f });
post.Add(new VignetteEffect());
post.Add(new FilmGrainEffect());

post.Get<BloomEffect>()!.Enabled = false;      // toggle or tweak at any time
post.Remove(someEffect);
```

| Effect | Stage | Needs |
|---|---|---|
| `BloomEffect` | HDR | threshold, soft knee, intensity, radius, iterations |
| `DepthOfFieldEffect` | HDR | scene depth; `FocusDistance` or `FocusTarget`, `FocusRange`, `MaxBlur` |
| `SunRaysEffect` | HDR | volumetric shafts in the air (the sun's shadow map + the clouds), seen in every direction; plus the glare around the sun while it is on screen |
| `ChromaticAberrationEffect` | HDR | `Strength` |
| `ColorCorrectionEffect` | LDR | `Exposure Temperature Tint HueShift Saturation Vibrance Contrast Brightness Lift Gamma Gain` (all neutral by default) |
| `ColorGradingEffect` | LDR | simple version: `Brightness Contrast Saturation Tint` |
| `VignetteEffect` | LDR | `Intensity Radius Softness Roundness` |
| `FilmGrainEffect` | LDR | `Strength` |

### Parameters by name

Every setting of every effect (and of the tone mapping, as `"ToneMapping"`) can be read and written by name, so
scripts, key bindings or config files do not need to know the effect classes. Names are case-insensitive; numbers,
`(r, g, b)` tuples, arrays and enum names are converted for you.

```csharp
post.Set("Bloom", "Intensity", 0.8f);                    // returns false and logs a warning if it does not fit
post.Set("ColorCorrection", "Lift", (0.02f, 0f, 0.05f)); // Vector3 from a tuple
post.Set("ToneMapping", "ToneMap", "Reinhard");          // enum by name
post.Set("Vignette", "Enabled", false);
float? v = post.Get("Bloom", "Threshold") as float?;
foreach (var line in post.Describe()) Console.WriteLine(line);   // "Bloom.Intensity: Single = 0.8", ...

bloom.SetParameter("Radius", 1.5f); bloom.Parameters;    // the same, directly on an effect instance
ParameterAccess.TrySet(anyObject, "Name", value);        // works on any object's public settings
```

Supported types: `float`, `int`, `bool`, enums, `Vector2/3/4` and `Color4`. Settings of other types (such as
`DepthOfFieldEffect.FocusTarget`) are set in code. If two effects of one kind exist, the first one is used.

**Write your own**: derive from `SinglePassEffect` (one fullscreen shader), or from `PostEffect` for multi-pass
effects. Put the fragment shader in `Shaders/fx_myeffect.frag` (it receives `vUV`, `uInput`, `uDepth`,
`uTexel`, `uAspect`), return its name from `ShaderName`, and set uniforms in `SetUniforms`. Effect shaders are
included in F5 hot reload. Override `Stage` to `PostStage.Ldr` to run after tone mapping, and `ShouldRun` to skip
the effect on frames where it has nothing to do.

## Notes

- Debug builds request a GL debug context and log driver messages; where that is unavailable (macOS) `glGetError`
  is polled once per frame instead.
- MSAA (4x) renders into an offscreen HDR target and is resolved before tone mapping; it falls back to no AA if
  the driver cannot create it.
- The skybox is drawn after opaque and before transparent objects so blended surfaces show the sky behind them.
