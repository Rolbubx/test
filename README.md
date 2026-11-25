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
- Linux: `libglfw3`, `libopenal1` and `libassimp5` (or the equivalent for your distribution)

## Getting started

```bash
git clone https://github.com/Rolbubx/test.git
cd test
dotnet run --project SolciteEngine -c Editor
```

The editor opens the **project launcher**. Create a project, or open an existing project folder (type its path, or drop
the folder onto the window). In Rider, the shared run configurations **SolciteEngine Editor** and **SolciteEngine Game**
are in `.run/`.

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
`%AppData%/SolciteEngine/recent-projects.json` (`~/.config/SolciteEngine/` on Linux).

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
| `--sandbox` | open the engine's own folder as a project |
| `--launcher` | show the launcher even with `--frames` |
| `--frames N` | close after N frames (smoke tests) |
| `--screenshot <file.png>` | with `--frames`: save the last frame |
| `--camera x,y,z,tx,ty,tz` | fix the view (screenshots) |
| `--novsync` | uncap the frame rate |
| `--debug` | enable the debug flag |

## Continuous integration

GitHub Actions (`.github/workflows/`):

- **CI** (`ci.yml`) runs on pushes to `main`/`master` and on pull requests. It builds the `Editor` and `Game`
  configurations on Ubuntu and Windows, builds the generated game project, and checks that the editor build contains no
  game.
- **Release** (`release.yml`) runs when a `v*` tag is pushed. It publishes the editor and the game for `win-x64` and
  `linux-x64`, checks that the editor package contains no game, and attaches the archives to a GitHub release. It can
  also be started manually to produce the archives as workflow artifacts.

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
