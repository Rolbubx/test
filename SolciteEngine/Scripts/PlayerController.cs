/// <summary>
/// Arrow keys (and WASD when a game camera is in charge) walk relative to the camera, Shift runs, Space jumps. Uses
/// gravity and collides with everything solid (imported FBX/glTF models, terrain, primitives) through
/// <c>MoveAndSlide</c>. Assets &gt; World &gt; Player adds a capsule with this script and a camera that follows it.
/// </summary>
public class PlayerController : Script
{
    public float Speed = 4f;
    public float RunSpeed = 7f;
    public float JumpSpeed = 6f;
    public float Gravity = 15f;

    /// <summary>Radius of the sphere used for collisions.</summary>
    public float Radius = 0.5f;

    /// <summary>Height of that sphere's centre relative to the object's centre (negative = lower: a capsule puts it in its feet).</summary>
    public float SphereOffset = 0f;

    /// <summary>How quickly the object turns to face where it walks (0 = it never turns).</summary>
    public float TurnSpeed = 10f;

    private Vector3 _velocity;

    public override void OnUpdate(float deltaTime)
    {
        // "Forward" is where the camera looks, flattened onto the ground (Camera.Yaw works even looking straight down).
        float yaw = Game.Camera.Yaw * MathF.PI / 180f;
        var forward = new Vector3(MathF.Cos(yaw), 0f, MathF.Sin(yaw));
        var right = Vector3.Cross(forward, Vector3.UnitY);

        // WASD fly the editor camera unless a camera script (GameCamera) turned FreeCamera off.
        bool wasd = !Game.FreeCamera;
        var move = Vector3.Zero;
        if (Game.IsKeyDown(Keys.Up) || (wasd && Game.IsKeyDown(Keys.W))) move += forward;
        if (Game.IsKeyDown(Keys.Down) || (wasd && Game.IsKeyDown(Keys.S))) move -= forward;
        if (Game.IsKeyDown(Keys.Right) || (wasd && Game.IsKeyDown(Keys.D))) move += right;
        if (Game.IsKeyDown(Keys.Left) || (wasd && Game.IsKeyDown(Keys.A))) move -= right;

        bool running = Game.IsKeyDown(Keys.LeftShift) || Game.IsKeyDown(Keys.RightShift);
        if (move.LengthSquared > 0f)
        {
            move = Vector3.Normalize(move);
            TurnTowards(move, deltaTime);
            move *= running ? RunSpeed : Speed;
        }

        _velocity.X = move.X;
        _velocity.Z = move.Z;
        _velocity.Y -= Gravity * deltaTime;

        var result = Self.MoveAndSlide(_velocity * deltaTime, Radius, new Vector3(0f, SphereOffset, 0f));
        if (result.Grounded && _velocity.Y < 0f) _velocity.Y = 0f;
        if (result.Grounded && Game.WasKeyPressed(Keys.Space)) _velocity.Y = JumpSpeed;
    }

    /// <summary>Turns the object (its forward is -Z) smoothly towards <paramref name="direction"/>.</summary>
    private void TurnTowards(Vector3 direction, float deltaTime)
    {
        if (TurnSpeed <= 0f) return;
        var target = Quaternion.FromAxisAngle(Vector3.UnitY, MathF.Atan2(-direction.X, -direction.Z));
        Self.Rotation = Quaternion.Slerp(Self.Rotation, target, 1f - MathF.Exp(-TurnSpeed * deltaTime));
    }
}
