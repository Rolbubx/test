using GT67.Utils;
using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>Position, rotation (quaternion) and scale with optional parent.</summary>
public sealed class Transform
{
    private Vector3 _position;
    private Quaternion _rotation = Quaternion.Identity;
    private Vector3 _euler;
    private Vector3 _scale = Vector3.One;

    public Transform? Parent { get; internal set; }

    public Vector3 Position
    {
        get => _position;
        set => _position = value;
    }

    public Quaternion Rotation
    {
        get => _rotation;
        set
        {
            _rotation = value.LengthSquared > 0 ? value.Normalized() : Quaternion.Identity;
            _euler = MathUtil.ToEulerDegrees(_rotation);
        }
    }

    /// <summary>Euler angles in degrees. The value you set is the value you read back.</summary>
    public Vector3 EulerAngles
    {
        get => _euler;
        set
        {
            _euler = value;
            _rotation = MathUtil.FromEulerDegrees(value);
        }
    }

    public Vector3 Scale
    {
        get => _scale;
        set => _scale = value;
    }

    public Matrix4 LocalMatrix =>
        Matrix4.CreateScale(_scale) * Matrix4.CreateFromQuaternion(_rotation) * Matrix4.CreateTranslation(_position);

    public Matrix4 WorldMatrix => Parent == null ? LocalMatrix : LocalMatrix * Parent.WorldMatrix;

    public Quaternion WorldRotation => Parent == null ? _rotation : (_rotation * Parent.WorldRotation).Normalized();

    public Vector3 WorldPosition => Parent == null ? _position : WorldMatrix.ExtractTranslation();

    public Vector3 Forward => Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, WorldRotation));
    public Vector3 Right => Vector3.Normalize(Vector3.Transform(Vector3.UnitX, WorldRotation));
    public Vector3 Up => Vector3.Normalize(Vector3.Transform(Vector3.UnitY, WorldRotation));

    /// <summary>Rotates around an axis expressed in the parent's (or world) space.</summary>
    public void Rotate(Vector3 axis, float degrees)
    {
        if (axis.LengthSquared < 1e-12f) return;
        var delta = Quaternion.FromAxisAngle(Vector3.Normalize(axis), degrees * MathUtil.Deg2Rad);
        Rotation = _rotation * delta;
    }

    /// <summary>Orients the -Z axis towards a world-space point.</summary>
    public void LookAt(Vector3 worldTarget, Vector3? up = null)
    {
        var worldRotation = MathUtil.LookRotation(worldTarget - WorldPosition, up ?? Vector3.UnitY);
        Rotation = Parent == null
            ? worldRotation
            : worldRotation * Parent.WorldRotation.Inverted();
    }
}
