using GT67.Core;
using OpenTK.Mathematics;

namespace GT67.Editor;

/// <summary>
/// Something the transform gizmo can move, and maybe rotate and scale: an object or a light. Everything goes through
/// world-space getters and setters so the gizmo does not care about parents.
/// </summary>
public sealed class GizmoTarget
{
    public GizmoTarget(Func<Vector3> getPosition, Action<Vector3> setPosition)
    {
        GetPosition = getPosition;
        SetPosition = setPosition;
    }

    public Func<Vector3> GetPosition { get; }
    public Action<Vector3> SetPosition { get; }

    /// <summary>World rotation; null for things that cannot be rotated (lights).</summary>
    public Func<Quaternion>? GetRotation { get; init; }
    public Action<Quaternion>? SetRotation { get; init; }

    /// <summary>Local scale; null for things that cannot be scaled.</summary>
    public Func<Vector3>? GetScale { get; init; }
    public Action<Vector3>? SetScale { get; init; }

    public bool CanRotate => GetRotation != null && SetRotation != null;
    public bool CanScale => GetScale != null && SetScale != null;

    public static GizmoTarget For(GameObject obj) => new(
        () => obj.WorldPosition,
        world => obj.Transform.Position = obj.Parent == null
            ? world
            : Vector3.TransformPosition(world, Matrix4.Invert(obj.Parent.Transform.WorldMatrix)))
    {
        GetRotation = () => obj.Transform.WorldRotation,
        SetRotation = world => obj.Transform.Rotation = obj.Parent == null
            ? world
            : (world * obj.Parent.Transform.WorldRotation.Inverted()).Normalized(),
        GetScale = () => obj.Scale,
        SetScale = scale => obj.Scale = scale,
    };

    // An attached light keeps where it was dragged to, relative to its object
    public static GizmoTarget For(PointLight light) => new(() => light.Position, p =>
    {
        light.Position = p;
        if (light.Follow != null) light.AttachTo(light.Follow);
    });

    public static GizmoTarget For(SpotLight light) => new(() => light.Position, p =>
    {
        light.Position = p;
        if (light.Follow != null) light.AttachTo(light.Follow);
    });

    /// <summary>Water moves (the Y arrow changes its level) and scales: X and Z resize it, the height does nothing.</summary>
    public static GizmoTarget For(Water water) => new(() => water.Position, p => water.Position = p)
    {
        GetScale = () => new Vector3(water.Width, 1f, water.Depth),
        SetScale = scale =>
        {
            water.Width = scale.X;
            water.Depth = scale.Z;
        },
    };
}
