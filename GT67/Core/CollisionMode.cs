namespace GT67.Core;

/// <summary>How a <see cref="GameObject"/> takes part in collision queries (<see cref="Scene.MoveAndSlide"/>).</summary>
public enum CollisionMode
{
    /// <summary>Not solid: things pass through.</summary>
    None,

    /// <summary>The model's bounding box (cheap; fine for crates and rough shapes).</summary>
    Box,

    /// <summary>The model's bounding sphere.</summary>
    Sphere,

    /// <summary>Every triangle of the model (exact; uses a BVH so large imported models stay fast).</summary>
    Mesh,
}
