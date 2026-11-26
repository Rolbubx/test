using OpenTK.Mathematics;
using SolciteEngine.Utils;

namespace SolciteEngine.Tests;

public class MathUtilTests
{
    [Theory]
    [InlineData(5f, 0f, 10f, 5f)]
    [InlineData(-1f, 0f, 10f, 0f)]
    [InlineData(11f, 0f, 10f, 10f)]
    public void Clamp(float v, float min, float max, float expected) => Assert.Equal(expected, MathUtil.Clamp(v, min, max));

    [Fact]
    public void RayHitsTriangleInFront()
    {
        bool hit = MathUtil.RayTriangle(new Vector3(0.2f, 0.2f, 5f), -Vector3.UnitZ,
            Vector3.Zero, Vector3.UnitX, Vector3.UnitY, out float t);
        Assert.True(hit);
        Assert.Equal(5f, t, 3);
    }

    [Fact]
    public void RayMissesTriangleBesideIt()
    {
        Assert.False(MathUtil.RayTriangle(new Vector3(2f, 2f, 5f), -Vector3.UnitZ,
            Vector3.Zero, Vector3.UnitX, Vector3.UnitY, out _));
    }

    [Fact]
    public void EulerDegrees_RoundTrip()
    {
        var degrees = new Vector3(10f, 20f, 30f);
        var back = MathUtil.ToEulerDegrees(MathUtil.FromEulerDegrees(degrees));
        Assert.True((back - degrees).Length < 0.01f, $"got {back}");
    }
}
