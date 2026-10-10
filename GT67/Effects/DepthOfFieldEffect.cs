using GT67.Core;
using OpenTK.Mathematics;

namespace GT67.Effects;

/// <summary>
/// Blurs everything away from the focus plane with a disc kernel. Focus on a fixed distance, or set
/// <see cref="FocusTarget"/> to follow an object.
/// </summary>
public sealed class DepthOfFieldEffect : SinglePassEffect
{
    private float _currentFocus = -1f;

    protected override string ShaderName => "fx_dof";

    /// <summary>Focus distance in world units (used when <see cref="FocusTarget"/> is null).</summary>
    public float FocusDistance { get; set; } = 10f;

    /// <summary>Keeps this object in focus.</summary>
    public GameObject? FocusTarget { get; set; }

    /// <summary>How quickly the focus follows its target (per second); 0 = instant.</summary>
    public float FocusSpeed { get; set; } = 6f;

    /// <summary>Distance from the focus plane at which blur reaches its maximum.</summary>
    public float FocusRange { get; set; } = 8f;

    /// <summary>Maximum blur radius in pixels.</summary>
    public float MaxBlur { get; set; } = 8f;

    protected override void SetUniforms(Shader shader, PostContext context)
    {
        var camera = context.Camera;
        float target = FocusDistance;
        if (FocusTarget is { IsDestroyed: false } obj)
            target = Vector3.Dot(obj.WorldPosition - camera.Position, camera.Front);
        target = MathF.Max(target, camera.Near);

        if (_currentFocus < 0f || FocusSpeed <= 0f)
            _currentFocus = target;
        else
            _currentFocus += (target - _currentFocus) * MathF.Min(1f, FocusSpeed * context.DeltaTime);

        shader.SetFloat("uNear", camera.Near);
        shader.SetFloat("uFar", camera.Far);
        shader.SetFloat("uFocus", _currentFocus);
        shader.SetFloat("uRange", MathF.Max(FocusRange, 0.01f));
        shader.SetFloat("uMaxBlur", MaxBlur);
    }
}
