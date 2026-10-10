using GT67.Utils;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GT67.Core;

/// <summary>Free-fly camera: WASD + Q/E, mouse look, scroll zoom, shift to sprint.</summary>
public sealed class Camera
{
    private float _yaw = -90f;
    private float _pitch;

    public Vector3 Position { get; set; }
    public float Fov { get; set; } = 60f;
    public float Near { get; set; } = 0.1f;
    public float Far { get; set; } = 500f;
    public float AspectRatio { get; set; } = 16f / 9f;
    public float MoveSpeed { get; set; } = 6f;
    public float SprintMultiplier { get; set; } = 3f;
    public float MouseSensitivity { get; set; } = 0.12f;

    public Camera(Vector3 position, float aspectRatio)
    {
        Position = position;
        AspectRatio = aspectRatio;
    }

    public float Yaw
    {
        get => _yaw;
        set => _yaw = value;
    }

    public float Pitch
    {
        get => _pitch;
        set => _pitch = MathUtil.Clamp(value, -89f, 89f);
    }

    public Vector3 Front
    {
        get
        {
            float yaw = _yaw * MathUtil.Deg2Rad, pitch = _pitch * MathUtil.Deg2Rad;
            return Vector3.Normalize(new Vector3(
                MathF.Cos(pitch) * MathF.Cos(yaw),
                MathF.Sin(pitch),
                MathF.Cos(pitch) * MathF.Sin(yaw)));
        }
    }

    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitY));
    public Vector3 Up => Vector3.Normalize(Vector3.Cross(Right, Front));

    public Matrix4 ViewMatrix => Matrix4.LookAt(Position, Position + Front, Vector3.UnitY);

    public Matrix4 ProjectionMatrix =>
        Matrix4.CreatePerspectiveFieldOfView(Fov * MathUtil.Deg2Rad, AspectRatio, Near, Far);

    public void Move(KeyboardState keys, float deltaTime)
    {
        float speed = MoveSpeed * (keys.IsKeyDown(Keys.LeftShift) ? SprintMultiplier : 1f) * deltaTime;
        var front = Front;
        var right = Right;

        if (keys.IsKeyDown(Keys.W)) Position += front * speed;
        if (keys.IsKeyDown(Keys.S)) Position -= front * speed;
        if (keys.IsKeyDown(Keys.D)) Position += right * speed;
        if (keys.IsKeyDown(Keys.A)) Position -= right * speed;
        if (keys.IsKeyDown(Keys.E) || keys.IsKeyDown(Keys.Space)) Position += Vector3.UnitY * speed;
        if (keys.IsKeyDown(Keys.Q) || keys.IsKeyDown(Keys.LeftControl)) Position -= Vector3.UnitY * speed;
    }

    public void Look(float deltaX, float deltaY)
    {
        _yaw += deltaX * MouseSensitivity;
        Pitch = _pitch - deltaY * MouseSensitivity;
    }

    public void Zoom(float scroll) => Fov = MathUtil.Clamp(Fov - scroll * 2f, 10f, 100f);

    public void LookAt(Vector3 target)
    {
        var d = Vector3.Normalize(target - Position);
        Pitch = MathF.Asin(d.Y) * MathUtil.Rad2Deg;
        _yaw = MathF.Atan2(d.Z, d.X) * MathUtil.Rad2Deg;
    }

    /// <summary>World-space ray through a pixel (origin top-left, in the same units as <paramref name="viewportSize"/>).</summary>
    public Ray ScreenPointToRay(Vector2 screenPoint, Vector2 viewportSize)
    {
        float ndcX = 2f * screenPoint.X / viewportSize.X - 1f;
        float ndcY = 1f - 2f * screenPoint.Y / viewportSize.Y;
        var inverse = Matrix4.Invert(ViewMatrix * ProjectionMatrix);

        var near = Vector4.TransformRow(new Vector4(ndcX, ndcY, -1f, 1f), inverse);
        var far = Vector4.TransformRow(new Vector4(ndcX, ndcY, 1f, 1f), inverse);
        var nearPoint = near.Xyz / near.W;
        var farPoint = far.Xyz / far.W;
        return new Ray(nearPoint, farPoint - nearPoint);
    }
}
