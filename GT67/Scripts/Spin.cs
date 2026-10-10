/// <summary>Rotates its object around the vertical axis.</summary>
public class Spin : Script
{
    public float DegreesPerSecond = 45f;

    public override void OnUpdate(float deltaTime) => Self.RotateY(DegreesPerSecond * deltaTime);
}
