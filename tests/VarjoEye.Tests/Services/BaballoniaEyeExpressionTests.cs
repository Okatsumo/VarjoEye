using VarjoEye.Core.Services.Baballonia;
using Xunit;

namespace VarjoEye.Tests.Services;

public sealed class BaballoniaEyeExpressionTests
{
    [Fact]
    public void FromRaw_MapsOpennessAndSharesPitch()
    {
        var values = new float[] { 0.75f, 0.25f, 0.2f, 0.25f, 0.75f, 0.4f };
        var (left, right) = BaballoniaEyeExpression.FromRaw(values);

        Assert.Equal(0.8, left.Openness, 3);
        Assert.Equal(0.6, right.Openness, 3);
        Assert.Equal(left.GazeY, right.GazeY, 5);
        Assert.True(left.GazeX > right.GazeX);
    }

    [Fact]
    public void FromRaw_ClosedEyeDoesNotSteerOpenEye()
    {
        var values = new float[] { 0.9f, 0.1f, 0.95f, 0.2f, 0.8f, 0.05f };
        var (left, right) = BaballoniaEyeExpression.FromRaw(values);

        Assert.True(left.Openness < 0.1);
        Assert.True(right.Openness > 0.8);
        Assert.Equal(BaballoniaEyeExpression.ToAxis(0.8f), right.GazeX, 5);
        Assert.Equal(BaballoniaEyeExpression.ToAxis(0.2f), right.GazeY, 5);
        Assert.Equal(right.GazeX, left.GazeX, 5);
        Assert.Equal(right.GazeY, left.GazeY, 5);
    }

    [Fact]
    public void FromRaw_RejectsNonFiniteOpennessViaClamp()
    {
        var values = new float[] { 0.5f, 0.5f, 2f, 0.5f, 0.5f, -1f };
        var (left, right) = BaballoniaEyeExpression.FromRaw(values);
        Assert.Equal(0, left.Openness);
        Assert.Equal(1, right.Openness);
    }

    [Theory]
    [InlineData(0f, -1d)]
    [InlineData(0.5f, 0d)]
    [InlineData(1f, 1d)]
    public void ToAxis_MapsUnitInterval(float raw, double expected) =>
        Assert.Equal(expected, BaballoniaEyeExpression.ToAxis(raw), 5);

    [Fact]
    public void ToOpenness_InvertsLid() =>
        Assert.Equal(0.25, BaballoniaEyeExpression.ToOpenness(0.75f), 5);
}
