# SolciteEngine

[![CI](https://github.com/Rolbubx/test/actions/workflows/ci.yml/badge.svg)](https://github.com/Rolbubx/test/actions/workflows/ci.yml)

SolciteEngine is a 3D game engine and map editor written in C# on .NET 8 and OpenGL. Build a scene in the editor, script
it in C#, and ship a game that contains only the runtime it needs.

## Features

- **Map editor**: hierarchy, inspector, transform gizmos, terrain sculpting and painting, multiple scenes, prefabs, a
  game UI designer (HUDs and menus) and live C# script compilation.
- **Rendering**: physically based sky with volumetric clouds, sun with time of day, cascaded shadows, point and spot
  lights, FFT ocean water with reflections, instanced grass, rain, volumetric fog, MSAA, frustum and occlusion culling.
- **Post-processing**: bloom, depth of field, sun shafts, vignette, chromatic aberration, film grain, colour grading, and
  your own effects and screen shaders.
- **Assets**: FBX, glTF/GLB, OBJ and DAE models (Assimp), PNG/JPG textures, WAV/MP3/OGG audio.
- **Gameplay**: scene graph, collisions (`MoveAndSlide`, BVH mesh collision), scripting API, positional audio, a game
  UI system.
- **Projects**: each game is a self-contained folder with its scripts, assets and saved map.
- **Lean shipping**: a built game contains the game and the engine runtime only, never the editor.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A GPU and driver with OpenGL 4.1 core support (Windows, Linux and macOS)
- Linux: `libglfw3` and `libassimp5` (or the equivalent for your distribution)

## Getting started

```bash
git clone https://github.com/Rolbubx/test.git
cd test
dotnet run --project SolciteEngine -c Editor
```

The first time the editor starts it runs a short **setup** (see below), then opens the **project launcher**. Create a project, or open an existing project folder (type its path, or drop
the folder onto the window). In Rider, the shared run configurations **SolciteEngine Editor** and **SolciteEngine Game**
are in `.run/`.

### First launch

On its first start, and again after a graphics driver update, the editor shows a setup screen that:

1. checks the graphics driver (OpenGL 4.1 or newer is required) and reports the renderer and the anti-aliasing available;
2. checks the native libraries it needs (GLFW, Skia, Assimp) and tells you what to install if one is missing;
3. compiles every engine shader once and stores the linked GPU programs in a **shader cache**;
4. prepares the per-user folders and records the result in `setup.json`.

The shader cache (`shader-cache/<driver>/` in the engine's user data folder: `%LocalAppData%/SolciteEngine` on Windows,
`~/.local/share/SolciteEngine` on Linux) is keyed by the shader source and the exact driver, so later starts load the
programs from disk instead of compiling them, and a new driver gets a fresh cache. Games use the same cache. Delete the
folder at any time to rebuild it, or start the editor with `--setup` to run the setup again. (OpenGL itself is the
driver's: the engine uses it through OpenTK, so there is nothing to compile for it.)

### Projects

A project is a `Project-<name>` folder, laid out like a Unity project:

```
Project-MyGame/
  infos.json                 name, engine and creation date: marks the folder as a project
  assets/                    everything you author
    Scripts/                 C# scripts, compiled live by the editor
    Models/ Textures/ Sounds/ Skybox/ Prefabs/ Shaders/Custom/
    tree.glb
    tree.glb.reference       the asset's identity (GUID, type), next to every asset
  generated-game/            what Compile writes: the whole game (scenes, UI) as a small C# project
  cache/                     asset index and editor settings; rebuilt by the editor, safe to delete
```

Every file in `assets/` gets a `<file>.reference` companion with a stable GUID and its type, created and kept up to date
by the editor (orphans are removed, and `cache/asset-index.json` maps GUIDs to paths). Move or rename an asset together
with its `.reference` file to keep its identity. References are not shipped with a game.

**Compile** saves the map into `generated-game/`. Reopening the project compiles it in memory and shows the map as it was
left. The folder button in the toolbar returns to the launcher. Recent projects are stored in
`recent-projects.json` in the same user data folder.

### Version control

Each project can live in a git repository (GitHub or any other remote). In the editor, the toolbar's **up-arrow** button
opens the version control panel: start a repository (`cache/` and the build folders are ignored), commit all changes with
a message, set the GitHub remote URL, then pull and push. It needs `git` on the PATH, and git signed in to GitHub
(credential manager or SSH key). Press **Compile** before committing: the map is saved only by Compile.

### MCP server (drive the editor from an AI client)

Start the editor with `--mcp [port]` (default `8765`) and it serves seven tools over MCP (JSON-RPC over HTTP, localhost only,
`http://127.0.0.1:8765/mcp`). The calls run on the editor's thread, on the open scene:

| Tool | Arguments | What it does |
|---|---|---|
| `execute_script` | `content` | Compiles a C# snippet (method body, `scene` and `print(...)` in scope) and runs it |
| `create_object` | `type`, `name`, `properties` | Adds a Cube/Sphere/Plane/Cylinder/Cone/Capsule/Torus, PointLight or SpotLight; `properties` sets `position`, `rotation` (degrees), `scale`, `color` and any other public property |
| `delete_object` | `name` | Deletes an object (or light, sound) by name or `Parent/Child` path |
| `get_tree` | | The scene as JSON: objects, transforms, scripts, lights, available script types |
| `attach_component` | `type`, `path`, `where` | Attaches a script class (or a PointLight/SpotLight) to the object at `path`; `where` is `self` (default) or `children` |
| `create_script` | `content`, `name` | Writes `Scripts/<name>.cs` in the project and compiles the scripts (errors are returned) |
| `compile_game` | | Same as the Compile button: scripts, then the game into `generated-game/` |

Claude Code: `claude mcp add --transport http solcite http://127.0.0.1:8765/mcp`.

### Screenshots of editor menus

`--menu <projects|vcs|settings>` opens that toolbar menu at start; it works with `--frames` and `--screenshot`, e.g.
`dotnet run --project SolciteEngine -c Editor -- --project Project-MyGame --menu vcs --frames 10 --screenshot vcs.png`.

### Running and shipping a game

```bash
dotnet run --project Project-MyGame/generated-game                    # play it
dotnet publish Project-MyGame/generated-game -c Release -r win-x64   # ship it
```

A shipped game contains the game code, the engine runtime (without the editor or Roslyn), the shaders, the UI fonts, the
game's own assets and the native libraries (OpenGL/GLFW, OpenAL, Skia, Assimp) of **one** platform. The publish step
produces a trimmed, precompiled single executable with the native libraries beside it. Without `-r`, the build targets
the current machine only.

The editor itself ships no game: the `Editor` configuration leaves the generated game out of the program, and CI fails if
it ever contains one.

## Build configurations

| Configuration | Purpose |
|---|---|
| `Editor` | the map editor (debug build); starts on the project launcher |
| `Game` | the game runtime without the editor (optimised) |
| `Debug` / `Release` | the default builds; `--editor` / `--game` choose the mode |

```bash
dotnet build SolciteEngine -c Editor
dotnet build SolciteEngine -c Game
SolciteEngine/publish-game.ps1            # Windows: Native AOT executable (needs the Visual Studio C++ build tools)
SolciteEngine/publish-game.ps1 -NoAot     # Windows: trimmed ReadyToRun single-file executable
```

### Command line

| Option | Effect |
|---|---|
| `--editor` / `--game` | force editor or game mode |
| `--project <folder>` | open a project without the launcher |
| `--mcp [port]` | editor: serve the MCP tools on `http://127.0.0.1:<port>/mcp` (default 8765) |
| `--sandbox` | open the engine's own folder as a project |
| `--setup` | run the first-launch setup again |
| `--launcher` | show the launcher even with `--frames` |
| `--frames N` | close after N frames (smoke tests) |
| `--screenshot <file.png>` | with `--frames`: save the last frame |
| `--camera x,y,z,tx,ty,tz` | fix the view (screenshots) |
| `--novsync` | uncap the frame rate |
| `--debug` | enable the debug flag |

## Continuous integration

GitHub Actions (`.github/workflows/`):

- **CI/CD** (`ci.yml`) runs on every push and pull request. It builds the `Editor` and `Game` configurations on
  Ubuntu and Windows, builds the generated game project, checks that the editor build contains no game, and runs the
  tests (`tests/SolciteEngine.Tests`, xUnit).
- On a push to `main` (or a `v*` tag, or a manual run on `main`) it then publishes the editor, the game and the
  downloader (`SolciteEngineDownloader.exe`) for `win-x64` and `linux-x64` and attaches them to a GitHub release, **only
  if the test gate passes**: at least 97% of all tests pass and **no critical test fails**. A tag `v1.2.3` gives release
  `v1.2.3`; a push to `main` gives `v<Version>.<run number>` (e.g. `v0.1.0.42`, `<Version>` from `SolciteEngine.csproj`).
- Critical tests are marked `[Fact, Trait(Critical.Category, Critical.Value)]` (start-up, loading and saves). The gate is
  `.github/scripts/test_gate.py`; it reads the TRX reports and writes a summary on the workflow run. Run the tests
  locally with `dotnet test tests/SolciteEngine.Tests` (critical only: add `--filter Category=Critical`).

## Documentation

The [reference](docs/REFERENCE.md) covers the editor, scenes, scripting, importing models, custom shaders, prefabs,
terrain, grass, sky and clouds, water and post-processing.

## Dependencies

[OpenTK](https://opentk.net/) (windowing, OpenGL, OpenAL), [Silk.NET.Assimp](https://github.com/dotnet/Silk.NET)
(model import), [StbImageSharp](https://github.com/StbSharp/StbImageSharp) (textures),
[SkiaSharp](https://github.com/mono/SkiaSharp) (UI rendering), [NLayer](https://github.com/naudio/NLayer) and
[NVorbis](https://github.com/NVorbis/NVorbis) (audio decoding), and Roslyn (`Microsoft.CodeAnalysis.CSharp`) for the
editor's live script compilation. The bundled fonts, Inter and JetBrains Mono, keep their own licenses in
`SolciteEngine/Assets/Fonts/`.
