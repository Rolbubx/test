using GT67.Core;
using GT67.Utils;
using OpenTK.Mathematics;

namespace GT67.Editor;

/// <summary>Three coloured arrows on the selected object; drag one to move the object along that axis.</summary>
public sealed class MoveGizmo
{
    private static readonly Vector3[] Axes = { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
    private static readonly Color4[] Colors =
    {
        new(1f, 0.25f, 0.25f, 1f), new(0.3f, 0.9f, 0.3f, 1f), new(0.3f, 0.5f, 1f, 1f),
    };

    private readonly GameObject[] _arrows = new GameObject[3];
    private int _hovered = -1;
    private int _dragAxis = -1;
    private float _dragStart;
    private Vector3 _objectStart;
    private float _length = 1f;

    public MoveGizmo(Scene scene)
    {
        var euler = new[] { new Vector3(0, 0, -90), new Vector3(0, 0, 0), new Vector3(90, 0, 0) };
        for (int i = 0; i < 3; i++)
        {
            var arrow = new GameObject(Primitives.Arrow(1f, 0.025f, 0.22f, 0.075f), Vector3.Zero)
            {
                Name = "Gizmo " + "XYZ"[i],
                Color = Colors[i],
                Unlit = true,
                CastShadow = false,
                ReceiveShadow = false,
                Collision = CollisionMode.None,
                EditorOnly = true,
                Overlay = true,
                Visible = false,
            };
            arrow.EulerAngles = euler[i];
            scene.Add(arrow);
            _arrows[i] = arrow;
        }
    }

    public bool IsDragging => _dragAxis >= 0;

    /// <summary>Places the arrows on <paramref name="target"/> (hidden when null) at a constant on-screen size.</summary>
    public void Update(Camera camera, GameObject? target)
    {
        bool show = target != null && !target.IsDestroyed;
        foreach (var arrow in _arrows) arrow.Visible = show;
        if (!show) return;

        var origin = target!.WorldPosition;
        _length = MathF.Max((camera.Position - origin).Length * 0.18f, 0.3f);
        for (int i = 0; i < 3; i++)
        {
            _arrows[i].Position = origin;
            _arrows[i].UniformScale = _length;
            // Brighten the arrow under the cursor or being dragged.
            bool active = i == _hovered || i == _dragAxis;
            _arrows[i].Color = active ? new Color4(1f, 0.95f, 0.3f, 1f) : Colors[i];
        }
    }

    /// <summary>Highlights the arrow the ray points at (call while the mouse moves).</summary>
    public void Hover(Ray ray, GameObject? target)
    {
        _hovered = target == null || IsDragging ? _hovered : PickAxis(ray, target.WorldPosition);
    }

    /// <summary>Starts dragging if the ray touches an arrow. Returns true if it did.</summary>
    public bool TryBeginDrag(Ray ray, GameObject target)
    {
        int axis = PickAxis(ray, target.WorldPosition);
        if (axis < 0) return false;

        _dragAxis = axis;
        _objectStart = target.WorldPosition;
        _dragStart = ClosestAxisParameter(ray, _objectStart, Axes[axis]);
        return true;
    }

    /// <summary>Moves the target so the arrow follows the mouse.</summary>
    public void Drag(Ray ray, GameObject target)
    {
        if (_dragAxis < 0) return;

        float now = ClosestAxisParameter(ray, _objectStart, Axes[_dragAxis]);
        var world = _objectStart + Axes[_dragAxis] * (now - _dragStart);
        target.Transform.Position = target.Parent == null
            ? world
            : Vector3.TransformPosition(world, Matrix4.Invert(target.Parent.Transform.WorldMatrix));
    }

    public void EndDrag()
    {
        _dragAxis = -1;
        _hovered = -1;
    }

    // ---- ray / axis maths ------------------------------------------------------------------------

    /// <summary>The arrow whose line the ray passes closest to (within a few pixels' worth), or -1.</summary>
    private int PickAxis(Ray ray, Vector3 origin)
    {
        int best = -1;
        float bestDistance = _length * 0.12f; // pick radius
        for (int i = 0; i < 3; i++)
        {
            float s = Math.Clamp(ClosestAxisParameter(ray, origin, Axes[i]), 0f, _length);
            var point = origin + Axes[i] * s;
            float distance = Vector3.Cross(ray.Direction, point - ray.Origin).Length;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }

    /// <summary>Distance along the axis line (through <paramref name="origin"/>) at its closest approach to the ray.</summary>
    private static float ClosestAxisParameter(Ray ray, Vector3 origin, Vector3 axis)
    {
        var w = origin - ray.Origin;
        float b = Vector3.Dot(axis, ray.Direction);
        float dw = Vector3.Dot(ray.Direction, w);
        float e = Vector3.Dot(axis, w);
        float denominator = 1f - b * b;
        return denominator < 1e-4f ? 0f : (b * dw - e) / denominator;
    }
}
