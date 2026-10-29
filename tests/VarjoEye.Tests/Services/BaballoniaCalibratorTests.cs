using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;
using VarjoEye.Core.Services;
using VarjoEye.Core.Services.Baballonia;
using VarjoEye.Tests.Fakes;
using Xunit;

namespace VarjoEye.Tests.Services;

public sealed class BaballoniaCalibratorTests
{
    [Fact]
    public void BeginCollection_WaitsForNativeCalibration()
    {
        var tracking = new FakeTrackingDataProvider();
        var overlay = new FakeCalibrationOverlay();
        using var calibrator = new BaballoniaCalibrator(
            tracking,
            new FakeBaballoniaTrackingService(),
            overlay,
            new BaballoniaCalibrationStore(),
            new InMemoryLogService());

        calibrator.BeginCollection();

        Assert.True(calibrator.IsCollecting);
        Assert.Equal(CalibrationOverlayPhase.Waiting, overlay.CurrentHint.Phase);
        Assert.Contains("Waiting for Varjo calibration", calibrator.StatusText);
    }

    [Fact]
    public void NativeReady_StartsSettleThenSweep()
    {
        var tracking = new FakeTrackingDataProvider();
        var overlay = new FakeCalibrationOverlay();
        using var calibrator = new BaballoniaCalibrator(
            tracking,
            new FakeBaballoniaTrackingService(),
            overlay,
            new BaballoniaCalibrationStore(),
            new InMemoryLogService());
        calibrator.BeginCollection();
        tracking.Publish(Live(calibrating: true, ready: false));
        tracking.Publish(Live(calibrating: false, ready: true));

        Assert.Contains("Waiting 7 s", calibrator.StatusText);
        Assert.Equal(CalibrationOverlayPhase.Waiting, overlay.CurrentHint.Phase);
    }

    [Fact]
    public void HeadsetCalibration_StartsBabbleWithoutAppButton()
    {
        var tracking = new FakeTrackingDataProvider();
        var overlay = new FakeCalibrationOverlay();
        using var calibrator = new BaballoniaCalibrator(
            tracking,
            new FakeBaballoniaTrackingService(),
            overlay,
            new BaballoniaCalibrationStore(),
            new InMemoryLogService());

        tracking.Publish(Live(calibrating: true, ready: false));

        Assert.True(calibrator.IsCollecting);
        Assert.Equal(CalibrationOverlayPhase.Waiting, overlay.CurrentHint.Phase);
        Assert.Contains("Waiting for Varjo calibration", calibrator.StatusText);

        tracking.Publish(Live(calibrating: false, ready: true));

        Assert.True(calibrator.IsCollecting);
        Assert.Contains("Waiting 7 s", calibrator.StatusText);
    }

    [Fact]
    public void SettleAndSweep_UseVarjoClockNotWallClock()
    {
        var tracking = new FakeTrackingDataProvider { CurrentTimeNanoseconds = 1_000_000_000 };
        var overlay = new FakeCalibrationOverlay();
        using var calibrator = new BaballoniaCalibrator(
            tracking,
            new FakeBaballoniaTrackingService(),
            overlay,
            new BaballoniaCalibrationStore(),
            new InMemoryLogService());
        calibrator.BeginCollection();
        tracking.Publish(Live(calibrating: true, ready: false));
        tracking.Publish(Live(calibrating: false, ready: true));

        tracking.Publish(Live(calibrating: false, ready: true));
        Assert.Contains("Waiting 7 s", calibrator.StatusText);
        Assert.Equal(CalibrationOverlayPhase.Waiting, overlay.CurrentHint.Phase);

        tracking.CurrentTimeNanoseconds = 8_000_000_000;
        tracking.Publish(Live(calibrating: false, ready: true));

        Assert.Contains("Follow the purple target", calibrator.StatusText);
        Assert.Equal(CalibrationOverlayPhase.Sweep, overlay.CurrentHint.Phase);
        Assert.Equal(8_000_000_000, overlay.CurrentHint.PhaseStartedNanoseconds);
    }

    [Fact]
    public void PairsBabbleWithNearestVarjoGazeByCaptureTime()
    {
        var tracking = new FakeTrackingDataProvider { CurrentTimeNanoseconds = 1_000_000_000 };
        var babble = new FakeBaballoniaTrackingService();
        using var calibrator = new BaballoniaCalibrator(
            tracking,
            babble,
            new FakeCalibrationOverlay(),
            new BaballoniaCalibrationStore(),
            new InMemoryLogService());
        calibrator.BeginCollection();
        tracking.Publish(Live(calibrating: true, ready: false));
        tracking.Publish(Live(calibrating: false, ready: true));
        tracking.CurrentTimeNanoseconds = 8_000_000_000;
        tracking.Publish(Live(calibrating: false, ready: true));

        const long capture = 8_005_000_000;
        tracking.PublishGaze(Gaze(capture, 0.35, -0.2));
        babble.Publish(Babble(capture, 0.1, 0.05));
        tracking.PublishGaze(Gaze(capture + 13_000_000, 0.9, 0.9));

        Assert.Contains("Collecting Babble samples (1 pairs)", calibrator.StatusText);
    }

    [Fact]
    public void RejectsGazeFartherThanPairWindow()
    {
        var tracking = new FakeTrackingDataProvider { CurrentTimeNanoseconds = 1_000_000_000 };
        var babble = new FakeBaballoniaTrackingService();
        using var calibrator = new BaballoniaCalibrator(
            tracking,
            babble,
            new FakeCalibrationOverlay(),
            new BaballoniaCalibrationStore(),
            new InMemoryLogService());
        calibrator.BeginCollection();
        tracking.Publish(Live(calibrating: true, ready: false));
        tracking.Publish(Live(calibrating: false, ready: true));
        tracking.CurrentTimeNanoseconds = 8_000_000_000;
        tracking.Publish(Live(calibrating: false, ready: true));

        tracking.PublishGaze(Gaze(8_005_000_000));
        babble.Publish(Babble(8_025_000_000));
        tracking.PublishGaze(Gaze(8_040_000_000));

        Assert.DoesNotContain("Collecting Babble samples", calibrator.StatusText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetachedFit_DoesNotHoldCollectionLock_AndCannotOverwriteNewSession(bool dispose)
    {
        var tracking = new FakeTrackingDataProvider { CurrentTimeNanoseconds = 1_000_000_000 };
        var logs = new InMemoryLogService();
        using var calibrator = new BaballoniaCalibrator(tracking, new FakeBaballoniaTrackingService(),
            new FakeCalibrationOverlay(), new BaballoniaCalibrationStore(), logs);
        using var fitting = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        // Hold the detached fit at its diagnostic checkpoint, without a timing-sensitive large fit.
        logs.Changed += (_, _) =>
        {
            if (!logs.Entries.Any(e => e.Message.StartsWith("Babble calibration samples:")) || fitting.IsSet) return;
            fitting.Set();
            release.Wait(TimeSpan.FromSeconds(5));
        };
        calibrator.BeginCollection();
        tracking.Publish(Live(calibrating: true, ready: false));
        tracking.Publish(Live(calibrating: false, ready: true));
        tracking.CurrentTimeNanoseconds = 8_000_000_000;
        tracking.Publish(Live(calibrating: false, ready: true));
        tracking.CurrentTimeNanoseconds = 23_000_000_000;
        var fit = Task.Run(() => tracking.Publish(Live(calibrating: false, ready: true)));
        try
        {
            await Task.Run(() => Assert.True(fitting.Wait(TimeSpan.FromSeconds(3))));
            await Task.Run(() =>
            {
                Assert.False(calibrator.IsCollecting);
                tracking.PublishGaze(Gaze(23_000_000_000));
                if (dispose) calibrator.Dispose();
                else calibrator.BeginCollection();
            }).WaitAsync(TimeSpan.FromSeconds(2));
            var status = calibrator.StatusText;
            release.Set();
            await fit.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(status, calibrator.StatusText);
            Assert.Null(calibrator.Current);
            Assert.Equal(!dispose, calibrator.IsCollecting);
        }
        finally { release.Set(); await fit; }
    }

    private static GazeSample Gaze(long capture, double x = 0.2, double y = -0.1)
    {
        var eye = new EyeTrackingEye(1, 4, 12, 0.3, 0.5, Vector3Value.Zero, new Vector3Value(x, y, 1));
        return new GazeSample(1, eye, eye, capture, 2, 1, 3, 3);
    }

    private static BaballoniaTrackingSample Babble(long capture, double x = 0.1, double y = 0.05)
    {
        var eye = new BaballoniaEye(x, y, 1);
        return new BaballoniaTrackingSample(
            BaballoniaRuntimeStatus.Tracking,
            "Tracking",
            true,
            false,
            new BaballoniaEyeResult(1, DateTimeOffset.UtcNow, eye, eye, capture),
            CameraFrameState.Waiting(EyeSide.Left),
            CameraFrameState.Waiting(EyeSide.Right));
    }

    private static EyeTrackingState Live(bool calibrating, bool ready)
    {
        var headset = HeadsetState.For(HeadsetCatalog.Aero, ConnectionStatus.Connected) with
        {
            Name = "Varjo Aero",
            EyeTrackingStatus = ready ? "Ready" : calibrating ? "Calibrating" : "Waiting",
            IsGazeCalibrating = calibrating
        };
        return new EyeTrackingState(
            headset,
            CameraFrameState.Waiting(EyeSide.Left),
            CameraFrameState.Waiting(EyeSide.Right),
            EyeSourceState.Empty,
            EyeSourceState.Empty,
            true);
    }
}
