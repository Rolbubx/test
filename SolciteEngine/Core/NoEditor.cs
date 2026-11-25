#if GAME
using SolciteEngine.Core;
using OpenTK.Windowing.Common;

namespace SolciteEngine.Editor;

/// <summary>
/// Stand-in for the map editor in the Game build, which leaves the Editor folder out (see SolciteEngine.csproj). It is never
/// created, so <see cref="Window.Editor"/> stays null and these members are never called.
/// </summary>
public sealed class MapEditor : IDisposable
{
    private MapEditor() { }

    public bool Playing => true;
    public void Update(float dt) { }
    public void Draw(float dt) { }
    public void OnKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys key) { }
    public void OnFileDrop(string[] files) { }
    public void OnMouseDown(MouseButtonEventArgs e) { }
    public void DrawInspectorExtras(GameObject obj) { }
    public void Dispose() { }
}
#endif
