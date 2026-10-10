using GT67.Core;
using GT67.Utils;
using OpenTK.Mathematics;

namespace GT67.Editor;

public enum GizmoMode
{
    Move,
    Rotate,
    Scale,
}

public enum GizmoSpace
{
    World,
    Local,
}

/// <summary>
/// The manipulator drawn on the selected object: arrows to move, rings to rotate, cube handles to scale (the centre
/// cube scales all axes together). Handles are overlay objects in the scene; picking and dragging are done with rays.
/// </summary>
public sealed class TransformGizmo
{
    private const int Center = 3;

    /// <summary>Drag handle id for moving the object by dragging the object itself (not an arrow).</summary>
    private const int Free = 4;

    private static readonly Vector3[] Units = { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
    private static readonly Color4[] Colors =
    {
        new(1f, 0.25f, 0.25f, 1f), new(0.3f, 0.9f, 0.3f, 1f), new(0.3f, 0.5f, 1f, 1f),
    };
    private static readonly Color4 Active = new(1f, 0.95f, 0.3f, 1f);

    // Rotations taking the +Y axis of a handle mesh onto X, Y and Z.
    private static readonly Quaternion[] BaseRotation =
    {
        Quaternion.FromEulerAngles(0f, 0f, -MathF.PI / 2f),
        Quaternion.Identity,
        Quaternion.FromEulerAngles(MathF.PI / 2f, 0f, 0f),
    };

    private readonly GameObject[] _arrows = new GameObject[3];
    private readonly GameObject[] _scaleHandles = new GameObject[3];
    private readonly GameObject[] _rings = new GameObject[3];
    private readonly GameObject _centerCube;

    private Camera _camera = null!;
    private GizmoMode _mode;
    private Vector3 _origin;
    private Vector3[] _axes = { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
    private bool _worldAxes = true;
    private float _length = 1f;
    private int _hovered = -1;

    // drag state
    private int _dragHandle = -1;
    private Vector3 _startPosition, _startScale, _startVector, _planeNormal, _uniformAxis;
    private Vector3 _moveAxis, _lastMove;
    private bool _moveAlongAxis;
    private Quaternion _startRotation;
    private float _startParameter;

    public TransformGizmo(Scene scene)
    {
        for (int i = 0; i < 3; i++)
        {
            _arrows[i] = MakeHandle(scene, Primitives.Arrow(1f, 0.025f, 0.22f, 0.075f), "Move", i);
            _scaleHandles[i] = MakeHandle(scene, Primitives.ScaleHandle(1f, 0.025f, 0.14f), "Scale", i);
            _rings[i] = MakeHandle(scene, Primitives.Torus(1f, 0.014f, 72, 6), "Rotate", i);
        }
        _centerCube = new GameObject(Primitives.Cube(0.18f), Vector3.Zero)
        {
            Name = "Gizmo Scale All",
            Color = new Color4(0.9f, 0.9f, 0.9f, 1f),
            Unlit = true,
            CastShadow = false,
            ReceiveShadow = false,
            Collision = CollisionMode.None,
            EditorOnly = true,
            Overlay = true,
            Visible = false,
        };
        scene.Add(_centerCube);
    }

    private static GameObject MakeHandle(Scene scene, Mesh mesh, string kind, int axis)
    {
        var handle = new GameObject(mesh, Vector3.Zero)
        {
            Name = $"Gizmo {kind} {"XYZ"[axis]}",
            Color = Colors[axis],
            Unlit = true,
            CastShadow = false,
            ReceiveShadow = false,
            Collision = CollisionMode.None,
            EditorOnly = true,
            Overlay = true,
            Visible = false,
        };
        scene.Add(handle);
        return handle;
    }

    public bool IsDragging => _dragHandle >= 0;

    /// <summary>Length of the handles in world units (they keep a constant size on screen).</summary>
    public float HandleLength => _length;

    public bool SnapEnabled { get; set; }
    public float MoveSnap { get; set; } = 0.5f;
    public float RotateSnap { get; set; } = 15f;
    public float ScaleSnap { get; set; } = 0.1f;

    /// <summary>Shows the handles of <paramref name="mode"/> on <paramref name="target"/> (all hidden when null).</summary>
    public void Update(Camera camera, GizmoTarget? target, GizmoMode mode, GizmoSpace space)
    {
        _camera = camera;
        if (target != null && ((mode == GizmoMode.Rotate && !target.CanRotate) || (mode == GizmoMode.Scale && !target.CanScale)))
            mode = GizmoMode.Move;
        if (!IsDragging) _mode = mode;
        else mode = _mode;

        SetVisible(_arrows, target != null && mode == GizmoMode.Move);
        SetVisible(_scaleHandles, target != null && mode == GizmoMode.Scale);
        SetVisible(_rings, target != null && mode == GizmoMode.Rotate);
        _centerCube.Visible = target != null && mode == GizmoMode.Scale;
        if (target == null) return;

        // While dragging, the maths stays anchored where the drag started (re-anchoring on the moving object every
        // frame made it jump back and forth); the handles themselves follow the object.
        var at = target.GetPosition();
        if (!IsDragging) _origin = at;
        _length = MathF.Max((camera.Position - at).Length * 0.16f, 0.3f);

        // Scaling always follows the object's own axes; move and rotate follow the chosen space.
        bool local = (space == GizmoSpace.Local || mode == GizmoMode.Scale) && target.CanRotate;
        var basis = local ? target.GetRotation!() : Quaternion.Identity;
        _worldAxes = !local;
        for (int i = 0; i < 3; i++) _axes[i] = Vector3.Normalize(Vector3.Transform(Units[i], basis));

        var set = mode switch { GizmoMode.Move => _arrows, GizmoMode.Scale => _scaleHandles, _ => _rings };
        for (int i = 0; i < 3; i++)
        {
            set[i].Position = at;
            set[i].Rotation = (BaseRotation[i] * basis).Normalized();
            set[i].UniformScale = _length;
            bool active = i == _hovered || i == _dragHandle;
            set[i].Color = active ? Active : Colors[i];
        }

        _centerCube.Position = at;
        _centerCube.Rotation = basis;
        _centerCube.UniformScale = _length;
        _centerCube.Color = _hovered == Center || _dragHandle == Center ? Active : new Color4(0.9f, 0.9f, 0.9f, 1f);
    }

    private static void SetVisible(GameObject[] objects, bool visible)
    {
        foreach (var o in objects) o.Visible = visible;
    }

    /// <summary>Highlights the handle the ray points at (call while the mouse moves).</summary>
    public void Hover(Ray ray, GizmoTarget? target)
    {
        if (IsDragging) return;
        _hovered = target == null ? -1 : Pick(ray);
    }

    /// <summary>Starts dragging if the ray touches a handle. Returns true if it did.</summary>
    public bool TryBeginDrag(Ray ray, GizmoTarget target)
    {
        int handle = Pick(ray);
        if (handle < 0) return false;

        _dragHandle = handle;
        _startPosition = target.GetPosition();
        _startScale = target.GetScale?.Invoke() ?? Vector3.One;
        _startRotation = target.GetRotation?.Invoke() ?? Quaternion.Identity;
        _origin = _startPosition;

        switch (_mode)
        {
            case GizmoMode.Move:
                BeginMove(ray, _axes[handle], alongAxis: true);
                break;
            case GizmoMode.Scale when handle != Center:
                _startParameter = AxisParameter(ray, _origin, _axes[handle]);
                break;
            case GizmoMode.Scale:
                _planeNormal = -_camera.Front;
                _uniformAxis = Vector3.Normalize(_camera.Right + _camera.Up);
                _startParameter = UniformParameter(ray);
                break;
            case GizmoMode.Rotate:
                _planeNormal = _axes[handle];
                _startVector = PlaneVector(ray, _origin, _planeNormal);
                break;
        }
        return true;
    }

    /// <summary>
    /// Starts moving the target by dragging the object itself (no handle needed): it slides over the horizontal plane
    /// it stands on, or straight up and down when <paramref name="vertical"/>. <paramref name="ray"/> is the mouse ray
    /// at the moment the button went down, so the object does not jump to the cursor.
    /// </summary>
    public void BeginFreeDrag(Ray ray, GizmoTarget target, bool vertical)
    {
        _dragHandle = Free;
        _startPosition = _origin = target.GetPosition();
        if (vertical) BeginMove(ray, Vector3.UnitY, alongAxis: true);
        else BeginMove(ray, Vector3.UnitY, alongAxis: false);
    }

    /// <summary>
    /// Sets up a move: along <paramref name="axis"/> (on the plane through it that faces the camera best), or across
    /// the plane whose normal is <paramref name="axis"/>.
    /// </summary>
    private void BeginMove(Ray ray, Vector3 axis, bool alongAxis)
    {
        _moveAxis = axis;
        _moveAlongAxis = alongAxis;
        if (alongAxis)
        {
            // The plane containing the axis and as square to the view as possible: stable even when the arrow points
            // almost straight at the camera (the closest-point maths used before shot off to infinity there).
            var n = ray.Direction - axis * Vector3.Dot(ray.Direction, axis);
            if (n.LengthSquared < 1e-6f) n = Vector3.Cross(axis, MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX);
            _planeNormal = Vector3.Normalize(n);
        }
        else _planeNormal = axis;

        _lastMove = Vector3.Zero;
        _startVector = MovePlanePoint(ray) ?? _startPosition;
    }

    /// <summary>Where the ray meets the move plane, or null when that is behind the camera or absurdly far away.</summary>
    private Vector3? MovePlanePoint(Ray ray)
    {
        if (!RayPlane(ray, _startPosition, _planeNormal, out float t)) return null;
        float far = _camera.Far;
        return t > far ? null : ray.GetPoint(t);
    }

    private Vector3 MoveOffset(Ray ray)
    {
        if (MovePlanePoint(ray) is not { } point) return _lastMove; // keep the last good position
        var delta = point - _startVector;
        if (_moveAlongAxis) delta = _moveAxis * Vector3.Dot(delta, _moveAxis);
        else delta -= _moveAxis * Vector3.Dot(delta, _moveAxis);
        _lastMove = delta;
        return delta;
    }

    /// <summary>Snaps a moved position: to the world grid along world axes, else in steps along the drag axis.</summary>
    private Vector3 SnapMove(Vector3 offset)
    {
        if (!SnapEnabled || MoveSnap <= 0f) return _startPosition + offset;
        if (_moveAlongAxis && !(_worldAxes || _dragHandle == Free))
            return _startPosition + _moveAxis * Snap(Vector3.Dot(offset, _moveAxis), MoveSnap);

        // World-aligned: put the coordinates that move onto the grid, leave the others untouched.
        var p = _startPosition + offset;
        for (int i = 0; i < 3; i++)
            if (MathF.Abs(offset[i]) > 1e-6f || (_moveAlongAxis && MathF.Abs(_moveAxis[i]) > 0.5f))
                p[i] = MathF.Round(p[i] / MoveSnap) * MoveSnap;
        return p;
    }

    /// <summary>Applies the mouse movement to the target.</summary>
    public void Drag(Ray ray, GizmoTarget target)
    {
        if (_dragHandle < 0) return;

        if (_dragHandle == Free)
        {
            target.SetPosition(SnapMove(MoveOffset(ray)));
            return;
        }

        switch (_mode)
        {
            case GizmoMode.Move:
                target.SetPosition(SnapMove(MoveOffset(ray)));
                break;
            case GizmoMode.Scale:
            {
                if (target.SetScale == null) break;
                float now = _dragHandle == Center ? UniformParameter(ray) : AxisParameter(ray, _origin, _axes[_dragHandle]);
                float factor = MathF.Max(0.01f, 1f + (now - _startParameter) / _length);
                var scale = _startScale;
                if (_dragHandle == Center) scale *= factor;
                else scale[_dragHandle] *= factor;
                target.SetScale(new Vector3(SnapScale(scale.X), SnapScale(scale.Y), SnapScale(scale.Z)));
                break;
            }
            case GizmoMode.Rotate:
            {
                if (target.SetRotation == null) break;
                var v = PlaneVector(ray, _origin, _planeNormal);
                if (_startVector.LengthSquared < 1e-8f || v.LengthSquared < 1e-8f) break;

                float angle = MathF.Atan2(Vector3.Dot(_planeNormal, Vector3.Cross(_startVector, v)), Vector3.Dot(_startVector, v));
                float degrees = Snap(angle * MathUtil.Rad2Deg, RotateSnap);
                var delta = Quaternion.FromAxisAngle(_planeNormal, degrees * MathUtil.Deg2Rad);
                target.SetRotation((_startRotation * delta).Normalized());
                break;
            }
        }
    }

    public void EndDrag()
    {
        _dragHandle = -1;
        _hovered = -1;
    }

    private float Snap(float value, float step) => SnapEnabled && step > 0f ? MathF.Round(value / step) * step : value;

    private float SnapScale(float value) => MathF.Max(0.001f, SnapEnabled ? MathF.Round(value / ScaleSnap) * ScaleSnap : value);

    // ---- picking ---------------------------------------------------------------------------------

    private int Pick(Ray ray)
    {
        float tolerance = _length * 0.12f;

        if (_mode == GizmoMode.Rotate)
        {
            int best = -1;
            float bestT = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                if (!RayPlane(ray, _origin, _axes[i], out float t)) continue;
                float radius = (ray.GetPoint(t) - _origin).Length;
                if (MathF.Abs(radius - _length) < tolerance && t < bestT)
                {
                    bestT = t;
                    best = i;
                }
            }
            return best;
        }

        if (_mode == GizmoMode.Scale && DistanceToPoint(ray, _origin) < _length * 0.13f) return Center;

        int axis = -1;
        float bestDistance = tolerance;
        for (int i = 0; i < 3; i++)
        {
            float s = Math.Clamp(AxisParameter(ray, _origin, _axes[i]), 0f, _length);
            float distance = DistanceToPoint(ray, _origin + _axes[i] * s);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                axis = i;
            }
        }
        return axis;
    }

    private static float DistanceToPoint(Ray ray, Vector3 point) => Vector3.Cross(ray.Direction, point - ray.Origin).Length;

    /// <summary>Distance along the axis line (through <paramref name="origin"/>) at its closest approach to the ray.</summary>
    private static float AxisParameter(Ray ray, Vector3 origin, Vector3 axis)
    {
        var w = origin - ray.Origin;
        float b = Vector3.Dot(axis, ray.Direction);
        float dw = Vector3.Dot(ray.Direction, w);
        float e = Vector3.Dot(axis, w);
        float denominator = 1f - b * b;
        return denominator < 1e-4f ? 0f : (b * dw - e) / denominator;
    }

    private static bool RayPlane(Ray ray, Vector3 point, Vector3 normal, out float t)
    {
        float denominator = Vector3.Dot(normal, ray.Direction);
        if (MathF.Abs(denominator) < 1e-5f)
        {
            t = 0f;
            return false;
        }
        t = Vector3.Dot(normal, point - ray.Origin) / denominator;
        return t > 0f;
    }

    /// <summary>The vector from the origin to where the ray crosses the plane (zero if it does not).</summary>
    private static Vector3 PlaneVector(Ray ray, Vector3 origin, Vector3 normal) =>
        RayPlane(ray, origin, normal, out float t) ? ray.GetPoint(t) - origin : Vector3.Zero;

    /// <summary>Position along the screen diagonal while dragging the centre handle.</summary>
    private float UniformParameter(Ray ray) => Vector3.Dot(PlaneVector(ray, _origin, _planeNormal), _uniformAxis);
}
