using OpenTK.Mathematics;

namespace GT67.Core;

/// <summary>What a ray hit: see <c>Game.Raycast</c> and <c>Game.MouseHit</c>.</summary>
/// <param name="Object">The closest object the ray hit.</param>
/// <param name="Point">The world point where it hit.</param>
/// <param name="Distance">Distance from the ray's origin to <paramref name="Point"/>.</param>
public readonly record struct RaycastHit(GameObject Object, Vector3 Point, float Distance);
