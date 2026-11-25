/// <summary>How the game camera looks at the object tagged "Player".</summary>
public enum CameraMode
{
    /// <summary>Behind and above the player; the mouse turns around it.</summary>
    ThirdPerson,

    /// <summary>From the player's eyes; the mouse looks around.</summary>
    FirstPerson,

    /// <summary>High above the player, looking down at it.</summary>
    TopDown,

    /// <summary>Stays where the camera object is placed, looking where it points.</summary>
    Fixed,
}

/// <summary>
/// The game camera: what the player sees when the game runs. Follows the object tagged "Player" or stays where it was
/// placed (Fixed). Hold the right mouse button to look around (or turn on LockCursor so the mouse always does), scroll
/// to move closer. The camera object itself is hidden while the game runs.
/// </summary>
public class GameCamera : Script
{
    public CameraMode Mode = CameraMode.ThirdPerson;

    /// <summary>Distance from the player (third person, top-down). The mouse wheel changes it.</summary>
    public float Distance = 6f;

    /// <summary>Height above the player's centre that the camera looks at (eye height in first person).</summary>
    public float Height = 0.6f;

    public float FieldOfView = 60f;
    public float MouseSensitivity = 0.15f;

    /// <summary>Captures the mouse so moving it always turns the camera (no right button needed).</summary>
    public bool LockCursor;

    /// <summary>How quickly the camera catches up with the player (0 = sticks to it).</summary>
    public float Smoothing = 12f;

    /// <summary>Pulls the camera in front of walls that come between it and the player (third person).</summary>
    public bool AvoidWalls = true;

    public bool HideInGame = true;

    private GameObject? _target;
    private float _yaw, _pitch;
    private bool _snap = true;

    public override void OnStart()
    {
        if (HideInGame) Self.Visible = false;
        Game.FreeCamera = false; // WASD walks the player instead of flying the camera
        _target = Game.FindByTag("Player").FirstOrDefault(o => o != Self && o.Enabled);
        if (_target == null && Mode != CameraMode.Fixed)
            Game.Log("GameCamera: no object tagged 'Player', so the camera stays where it is.");

        // Start looking the way the camera object points (same angles as Camera.Yaw / Camera.Pitch).
        var f = Self.Forward;
        _yaw = MathF.Atan2(f.Z, f.X) * 180f / MathF.PI;
        _pitch = Math.Clamp(MathF.Asin(Math.Clamp(f.Y, -1f, 1f)) * 180f / MathF.PI, -80f, 80f);

        if (LockCursor && Mode != CameraMode.Fixed) Game.CursorLocked = true;
    }

    public override void OnLateUpdate(float deltaTime)
    {
        if (Game.ViewDetached)
        {
            _snap = true; // the editor's "Free view": leave the camera alone, jump back afterwards
            return;
        }

        var camera = Game.Camera;
        camera.Fov = FieldOfView;
        float dt = Game.UnscaledDeltaTime; // keeps working while the game is paused

        if (Mode == CameraMode.Fixed || _target == null || _target.IsDestroyed)
        {
            camera.Position = Self.WorldPosition;
            camera.LookAt(Self.WorldPosition + Self.Forward);
            return;
        }

        if (Game.CursorLocked || Game.IsMouseDown(MouseButton.Right))
        {
            var d = Game.MouseDelta;
            _yaw += d.X * MouseSensitivity;
            _pitch = Math.Clamp(_pitch - d.Y * MouseSensitivity, -85f, 85f);
        }
        if (Mode != CameraMode.FirstPerson) Distance = Math.Clamp(Distance - Game.MouseScroll * 0.8f, 1.5f, 60f);

        var pivot = _target.WorldPosition + Vector3.UnitY * Height;
        Vector3 desired;
        switch (Mode)
        {
            case CameraMode.FirstPerson:
                camera.Yaw = _yaw;
                camera.Pitch = _pitch;
                camera.Position = pivot;
                _snap = false;
                return;

            case CameraMode.TopDown:
                desired = pivot + new Vector3(0f, Distance, Distance * 0.4f);
                break;

            default:
                camera.Yaw = _yaw;
                camera.Pitch = _pitch;
                desired = pivot - camera.Front * Distance;
                if (AvoidWalls) desired = PullInFrontOfWalls(pivot, desired);
                break;
        }

        camera.Position = _snap || Smoothing <= 0f
            ? desired
            : Vector3.Lerp(camera.Position, desired, 1f - MathF.Exp(-Smoothing * dt));
        if (Mode == CameraMode.TopDown) camera.LookAt(pivot);
        _snap = false;
    }

    /// <summary>The camera position moved towards the player if something solid is in between.</summary>
    private Vector3 PullInFrontOfWalls(Vector3 pivot, Vector3 desired)
    {
        var toCamera = desired - pivot;
        float distance = toCamera.Length;
        if (distance < 1e-3f) return desired;

        // The player is hidden from the ray for a moment so it does not block its own camera.
        bool visible = _target!.Visible;
        _target.Visible = false;
        var hit = Game.Raycast(pivot, toCamera, distance);
        _target.Visible = visible;

        return hit is { } h ? pivot + toCamera / distance * MathF.Max(h.Distance - 0.25f, 0.3f) : desired;
    }

    public override void OnDestroy()
    {
        if (LockCursor && Self.Scene?.Game is { } game) game.CursorLocked = false;
    }
}
