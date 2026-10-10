using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using GT67.Core;

namespace GT67.Scripting;

/// <summary>
/// Base class for behaviour you attach to a <see cref="GameObject"/>. Write a class in <c>Scripts/*.cs</c>, override
/// the hooks you need, and attach it in the editor (<c>obj.AddScript(new Spin())</c> in code). Public fields and
/// properties of simple types (float, int, bool, enums, Vector2/3/4, Color4) show up in the editor and are saved.
/// </summary>
/// <example>
/// <code>
/// public class Spin : Script
/// {
///     public float DegreesPerSecond = 90f;
///     public override void OnUpdate(float dt) =&gt; Self.RotateY(DegreesPerSecond * dt);
/// }
/// </code>
/// </example>
public abstract class Script
{
    private bool _handlesDown, _handlesHeld, _handlesUp, _inspected;

    /// <summary>The object this script is attached to.</summary>
    public GameObject Self { get; internal set; } = null!;

    /// <summary>The scene, camera, input and helpers (null until the object is in a scene running in a window).</summary>
    public Game Game => Self.Scene?.Game ?? throw new InvalidOperationException("The script's object is not in a running scene.");

    public Scene Scene => Self.Scene ?? throw new InvalidOperationException("The script's object is not in a scene.");

    /// <summary>A disabled script receives no callbacks.</summary>
    public bool Enabled { get; set; } = true;

    internal bool Started { get; set; }

    /// <summary>Called once, right before the first <see cref="OnUpdate"/> (when the scene starts running).</summary>
    public virtual void OnStart() { }

    /// <summary>Called every frame while the scene is running.</summary>
    public virtual void OnUpdate(float deltaTime) { }

    /// <summary>Called every frame after every object's <see cref="OnUpdate"/>: the place for cameras that follow something.</summary>
    public virtual void OnLateUpdate(float deltaTime) { }

    /// <summary>A key went down. <paramref name="mouseWorld"/> is the world point under the cursor.</summary>
    public virtual void OnKeyDown(Keys key, Vector3 mouseWorld, Vector3 cameraPosition) { }

    /// <summary>Called every frame for each key that is held down.</summary>
    public virtual void OnKeyHeld(Keys key, Vector3 mouseWorld, Vector3 cameraPosition) { }

    public virtual void OnKeyUp(Keys key, Vector3 mouseWorld, Vector3 cameraPosition) { }

    /// <summary>This object (or something it moved with <c>MoveAndSlide</c>) touched <paramref name="other"/>.</summary>
    public virtual void OnCollision(GameObject other, Vector3 normal) { }

    /// <summary>The script is removed or its object is destroyed.</summary>
    public virtual void OnDestroy() { }

    // ---- plumbing --------------------------------------------------------------------------------

    /// <summary>True if this script overrides the key hook for <paramref name="action"/> (so the engine can skip the rest).</summary>
    internal bool Handles(KeyAction action)
    {
        if (!_inspected)
        {
            _inspected = true;
            _handlesDown = Overrides(nameof(OnKeyDown));
            _handlesHeld = Overrides(nameof(OnKeyHeld));
            _handlesUp = Overrides(nameof(OnKeyUp));
        }
        return action switch
        {
            KeyAction.Down => _handlesDown,
            KeyAction.Held => _handlesHeld,
            _ => _handlesUp,
        };
    }

    internal void RaiseKey(KeyAction action, Keys key, Vector3 mouseWorld, Vector3 cameraPosition)
    {
        switch (action)
        {
            case KeyAction.Down: OnKeyDown(key, mouseWorld, cameraPosition); break;
            case KeyAction.Held: OnKeyHeld(key, mouseWorld, cameraPosition); break;
            default: OnKeyUp(key, mouseWorld, cameraPosition); break;
        }
    }

    private bool Overrides(string method) =>
        GetType().GetMethod(method, new[] { typeof(Keys), typeof(Vector3), typeof(Vector3) })?.DeclaringType != typeof(Script);
}
