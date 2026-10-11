using OpenTK.Mathematics;

namespace GT67.Core;

public abstract class Light
{
    public string Name { get; set; } = "Light";
    public bool Enabled { get; set; } = true;

    /// <summary>sRGB colour; converted to linear when uploaded.</summary>
    public Color4 Color { get; set; } = Color4.White;

    public float Intensity { get; set; } = 1f;

    /// <summary>
    /// Point and spot lights: the object this light is attached to (it moves and turns with it), or null. Set it with
    /// <c>AttachTo(obj)</c>, which keeps the light where it is; drag the light onto an object in the editor's Hierarchy.
    /// </summary>
    public GameObject? Follow { get; set; }

    /// <summary>Where the light sits in the followed object's own coordinates (set by <c>AttachTo</c>).</summary>
    public Vector3 FollowOffset { get; set; }

    /// <summary>Called by the scene every frame: places an attached light on its object (and forgets a destroyed one).</summary>
    internal abstract void FollowObject();

    /// <summary>A world position in the coordinates of <paramref name="obj"/>, and back.</summary>
    internal static Vector3 ToLocal(GameObject obj, Vector3 world) =>
        Vector3.TransformPosition(world, Matrix4.Invert(obj.Transform.WorldMatrix));

    internal static Vector3 ToWorld(GameObject obj, Vector3 local) =>
        Vector3.TransformPosition(local, obj.Transform.WorldMatrix);
}
