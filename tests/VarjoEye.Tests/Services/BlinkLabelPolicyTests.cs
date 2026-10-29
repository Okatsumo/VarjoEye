using VarjoEye.Core.Models;
using VarjoEye.Core.Services.Baballonia.Sampling;
using Xunit;

namespace VarjoEye.Tests.Services;

public sealed class BlinkLabelPolicyTests
{
    [Fact]
    public void GazePhase_AlwaysOpen()
    {
        Assert.True(BlinkLabelPolicy.TryLabel(false, Eye(0.1), out var lid));
        Assert.Equal(0, lid);
        Assert.True(BlinkLabelPolicy.TryLabel(false, null, out lid));
        Assert.Equal(0, lid);
    }

    [Fact]
    public void BlinkPhase_ClosedWhenOpennessBelow025()
    {
        Assert.True(BlinkLabelPolicy.TryLabel(true, Eye(0.24), out var lid));
        Assert.Equal(1, lid);
        Assert.True(BlinkLabelPolicy.TryLabel(true, Eye(0), out lid));
        Assert.Equal(1, lid);
    }

    [Fact]
    public void BlinkPhase_OpenWhenOpennessAtLeast075()
    {
        Assert.True(BlinkLabelPolicy.TryLabel(true, Eye(0.75), out var lid));
        Assert.Equal(0, lid);
        Assert.True(BlinkLabelPolicy.TryLabel(true, Eye(1), out lid));
        Assert.Equal(0, lid);
    }

    [Fact]
    public void BlinkPhase_RejectsPartialClosure()
    {
        Assert.False(BlinkLabelPolicy.TryLabel(true, Eye(0.25), out _));
        Assert.False(BlinkLabelPolicy.TryLabel(true, Eye(0.5), out _));
        Assert.False(BlinkLabelPolicy.TryLabel(true, Eye(0.74), out _));
    }

    [Fact]
    public void BlinkPhase_RejectsMissingOrInvalidOpenness()
    {
        Assert.False(BlinkLabelPolicy.TryLabel(true, null, out _));
        Assert.False(BlinkLabelPolicy.TryLabel(true, Eye(double.NaN), out _));
        Assert.False(BlinkLabelPolicy.TryLabel(true, Eye(double.PositiveInfinity), out _));
    }

    [Fact]
    public void LeftAndRightAreIndependent()
    {
        Assert.True(BlinkLabelPolicy.TryLabel(true, Eye(0.1), out var left));
        Assert.True(BlinkLabelPolicy.TryLabel(true, Eye(0.9), out var right));
        Assert.Equal(1, left);
        Assert.Equal(0, right);
    }

    private static EyeTrackingEye Eye(double openness) =>
        new(openness, 4, 12, 0.3, 0.5, Vector3Value.Zero, new Vector3Value(0, 0, 1));
}
