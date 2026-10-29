using VarjoEye.Core.Services.Baballonia;
using Xunit;

namespace VarjoEye.Tests.Services;

public sealed class BaballoniaSmoothingFilterTests
{
    [Fact]
    public void Disabled_PassesAllChannelsThroughAndReenablesWithoutOldHistory()
    {
        var filter = new BaballoniaSmoothingFilter();
        filter.Apply(new float[6], 1_000_000_000, true, 35);
        var values = Enumerable.Repeat(0.8f, 6).ToArray();
        filter.Apply(values, 1_010_000_000, false, 35);
        Assert.All(values, value => Assert.Equal(0.8f, value));
        var next = Enumerable.Repeat(0.7f, 6).ToArray();
        filter.Apply(next, 1_020_000_000, true, 35);
        Assert.All(next, value => Assert.Equal(0.7f, value));
    }

    [Fact]
    public void LowerStrength_RespondsFasterAndChangingStrengthResetsHistory()
    {
        var fast = Response(0);
        var legacy = Response(35);
        var slow = Response(100);
        Assert.True(fast > legacy && legacy > slow);
        var filter = new BaballoniaSmoothingFilter();
        filter.Apply(new float[6], 1_000_000_000, true, 100);
        var changed = Enumerable.Repeat(0.5f, 6).ToArray();
        filter.Apply(changed, 1_010_000_000, true, 0);
        Assert.All(changed, value => Assert.Equal(0.5f, value));
    }

    private static float Response(double strength)
    {
        var filter = new BaballoniaSmoothingFilter();
        filter.Apply(new float[6], 1_000_000_000, true, strength);
        var values = Enumerable.Repeat(0.01f, 6).ToArray();
        filter.Apply(values, 1_010_000_000, true, strength);
        return values[0];
    }

    [Fact]
    public void DefaultStrength_MatchesPreviousFilterEquation()
    {
        const double dt = 0.01;
        var derivativeAlpha = 2 * Math.PI * dt / (1 + 2 * Math.PI * dt);
        var cutoff = 0.5 + 3 * derivativeAlpha;
        var rate = 2 * Math.PI * cutoff * dt;
        var expected = (float)(rate / (1 + rate) * 0.01f);
        Assert.Equal(expected, Response(35), 6);
    }

    [Theory]
    [InlineData(-100)]
    [InlineData(200)]
    [InlineData(double.NaN)]
    public void InvalidStrength_StillProducesFiniteOutput(double strength) =>
        Assert.True(float.IsFinite(Response(strength)));
}
