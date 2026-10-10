using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GT67.Core;

/// <summary>
/// Keyboard callback attached to a <see cref="GameObject"/> (<c>OnKeyDown</c>, <c>OnKeyHeld</c>, <c>OnKeyUp</c>).
/// It is called automatically for every key and every object that has one set; leave it null to ignore keys.
/// </summary>
/// <param name="key">The key that was pressed, is held down, or was released.</param>
/// <param name="mouseWorldPosition">
/// The point in the world under the mouse cursor (or the screen centre while the cursor is captured): where the
/// cursor ray hits the scene, else where it crosses the ground plane (y = 0), else 100 units along the ray.
/// </param>
/// <param name="cameraPosition">The camera's world position.</param>
/// <param name="objectPosition">This object's world position.</param>
/// <param name="self">The object the callback belongs to (so one lambda can be shared by clones).</param>
/// <param name="deltaTime">Seconds since the previous frame.</param>
public delegate void KeyCallback(Keys key, Vector3 mouseWorldPosition, Vector3 cameraPosition,
    Vector3 objectPosition, GameObject self, float deltaTime);
