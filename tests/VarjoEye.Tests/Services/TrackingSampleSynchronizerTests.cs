using VarjoEye.Core.Models;
using VarjoEye.Core.Services.Baballonia.Sampling;
using Xunit;

namespace VarjoEye.Tests.Services;

public sealed class TrackingSampleSynchronizerTests
{
    [Fact]
    public void FindNearest_PairsWithinWindow()
    {
        var sync = new TrackingSampleSynchronizer(8, 12_000_000);
        sync.Push(Gaze(8_000_000_000, 0.1));
        sync.Push(Gaze(8_005_000_000, 0.2));

        var match = sync.FindNearest(8_010_000_000);

        Assert.NotNull(match);
        Assert.Equal(8_005_000_000, match.CaptureTimeNanoseconds);
    }

    [Fact]
    public void FindNearest_RejectsOutsideWindow()
    {
        var sync = new TrackingSampleSynchronizer(8, 12_000_000);
        sync.Push(Gaze(8_000_000_000));

        Assert.Null(sync.FindNearest(8_020_000_000));
    }

    [Fact]
    public void Push_DropsOldestWhenCapacityExceeded()
    {
        var sync = new TrackingSampleSynchronizer(2, 12_000_000);
        sync.Push(Gaze(1_000_000_000));
        sync.Push(Gaze(2_000_000_000));
        sync.Push(Gaze(3_000_000_000));

        Assert.Equal(2, sync.Count);
        Assert.Equal(3_000_000_000, sync.Latest!.CaptureTimeNanoseconds);
        Assert.Null(sync.FindNearest(1_000_000_000));
    }

    [Fact]
    public void RateLimiter_IsIndependentPerEye()
    {
        var limiter = new SampleRateLimiter(20_000_000);

        Assert.True(limiter.TryAccept(true, 100));
        Assert.False(limiter.TryAccept(true, 10_000_000));
        Assert.True(limiter.TryAccept(false, 10_000_000));
    }

    [Fact]
    public void RateLimiter_ResetAllowsImmediateSample()
    {
        var limiter = new SampleRateLimiter(20_000_000);
        Assert.True(limiter.TryAccept(true, 100));
        limiter.Reset();
        Assert.True(limiter.TryAccept(true, 101));
    }

    [Fact]
    public void Validator_RejectsNonFiniteFeaturesAndInvalidGaze()
    {
        var features = new float[2];
        features[0] = float.NaN;
        Assert.Equal(SampleRejectionReason.NonFiniteValues,
            TrackingSampleValidator.Features(features, new float[2], 2));
        Assert.Equal(SampleRejectionReason.InvalidTimestamp, TrackingSampleValidator.Timestamp(0));
        Assert.Equal(SampleRejectionReason.InvalidTrackingStatus, TrackingSampleValidator.GazeFrame(1, 1));
        Assert.Equal(SampleRejectionReason.LowStability, TrackingSampleValidator.GazeFrame(2, 0.5));
        Assert.Equal(SampleRejectionReason.None, TrackingSampleValidator.GazeFrame(2, 0.65));
    }

    private static GazeSample Gaze(long capture, double x = 0)
    {
        var eye = new EyeTrackingEye(1, 4, 12, 0.3, 0.5, Vector3Value.Zero, new Vector3Value(x, 0, 1));
        return new GazeSample(1, eye, eye, capture, 2, 1, 3, 3);
    }
}
