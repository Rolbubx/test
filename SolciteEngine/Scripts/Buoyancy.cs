/// <summary>
/// Makes its object float: it bobs on the waves (the real wave heights of the water it is on), tilts with their slope,
/// drifts with the wind and slows down in the water. Density decides how it floats: 0.5 rides half under, 0.2 high
/// like a cork, above 1 it sinks. Out of the water it falls until it lands on the ground. The water shows the splash
/// when it falls in and the wake when it moves.
/// </summary>
public class Buoyancy : Script
{
    /// <summary>Density compared with water: below 1 floats (lower = higher in the water), above 1 sinks.</summary>
    public float Density = 0.5f;

    /// <summary>How hard the water holds it back (higher = it settles faster and drifts slower).</summary>
    public float Drag = 1.5f;

    /// <summary>How readily it follows the tilt of the waves (0 = stays upright).</summary>
    public float Tilt = 1f;

    /// <summary>How much the wind pushes it across the water.</summary>
    public float Drift = 0.3f;

    public float Gravity = 9.81f;

    private Vector3 _velocity;
    private float _halfHeight = 0.5f, _halfWidth = 0.5f;
    private Quaternion _rest;

    public override void OnStart()
    {
        var size = Self.Bounds.Size;
        _halfHeight = MathF.Max(size.Y * 0.5f, 0.05f);
        _halfWidth = MathF.Max(MathF.Max(size.X, size.Z) * 0.5f, 0.1f);
        _rest = Self.Rotation;   // the waves tilt it from the way it was placed (a log lying down stays lying down)
    }

    public override void OnUpdate(float deltaTime)
    {
        if (deltaTime <= 0f) return;
        var position = Self.WorldPosition;
        var water = Game.WaterAt(position);

        // How much of the object is under the surface: 0 (above) .. 1 (all of it).
        float surface = water?.GetHeightAt(position.X, position.Z) ?? float.MinValue;
        float submerged = water == null ? 0f : Math.Clamp((surface - (position.Y - _halfHeight)) / (2f * _halfHeight), 0f, 1f);

        // Archimedes: the water pushes up with the weight of the water moved aside. At rest, submerged == Density.
        float lift = Gravity * submerged / MathF.Max(Density, 0.01f);
        _velocity.Y += (lift - Gravity) * deltaTime;
        _velocity *= MathF.Exp(-Drag * (0.15f + submerged) * deltaTime);
        // Bobbing up and down loses energy fast in water (it pushes water around): light objects settle instead of
        // bouncing out of it.
        _velocity.Y *= MathF.Exp(-4f * submerged / MathF.Max(MathF.Sqrt(Density), 0.3f) * deltaTime);

        if (water != null && submerged > 0f)
        {
            var wind = water.WindDirection.LengthSquared > 1e-6f ? water.WindDirection.Normalized() : Vector2.Zero;
            _velocity += new Vector3(wind.X, 0f, wind.Y) * Drift * submerged * deltaTime;
        }

        position += _velocity * deltaTime;

        // On dry ground: land and stop instead of falling forever (the object is hidden from its own ground test).
        bool visible = Self.Visible;
        Self.Visible = false;
        float ground = Game.GroundHeight(position.X, position.Z, float.MinValue);
        Self.Visible = visible;
        if (submerged <= 0f && ground > float.MinValue && position.Y - _halfHeight < ground)
        {
            position.Y = ground + _halfHeight;
            _velocity = Vector3.Zero;
        }
        Self.Position = position;

        // Tilt with the waves: the surface slope across the object's width.
        if (water != null && submerged > 0f && Tilt > 0f)
        {
            float r = _halfWidth;
            float dx = (water.GetHeightAt(position.X + r, position.Z) - water.GetHeightAt(position.X - r, position.Z)) / (2f * r);
            float dz = (water.GetHeightAt(position.X, position.Z + r) - water.GetHeightAt(position.X, position.Z - r)) / (2f * r);
            var up = Vector3.Normalize(new Vector3(-dx * Tilt, 1f, -dz * Tilt));
            var axis = Vector3.Cross(Vector3.UnitY, up);
            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.UnitY, up), -1f, 1f));
            var tilt = axis.LengthSquared > 1e-8f ? Quaternion.FromAxisAngle(Vector3.Normalize(axis), angle) : Quaternion.Identity;
            var target = tilt * _rest;
            Self.Rotation = Quaternion.Slerp(Self.Rotation, target, 1f - MathF.Exp(-4f * deltaTime));
        }
    }
}
