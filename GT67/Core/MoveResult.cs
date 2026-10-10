using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>Outcome of <see cref="Scene.MoveAndSlide"/>.</summary>
/// <param name="Position">Where the sphere ended up, pushed out of everything solid.</param>
/// <param name="Collided">True if anything pushed the sphere.</param>
/// <param name="Grounded">True if something pushed it upwards (the surface under it is walkable, normal.Y &gt; 0.5).</param>
/// <param name="GroundNormal">Normal of the strongest upward-facing contact (up when not grounded).</param>
/// <param name="Ground">The object standing under the sphere, if grounded.</param>
public readonly record struct MoveResult(Vector3 Position, bool Collided, bool Grounded, Vector3 GroundNormal,
    GameObject? Ground);
