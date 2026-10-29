using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;
using VarjoEye.Core.Services;
using VarjoEye.Core.Services.Baballonia;
using VarjoEye.Tests.Fakes;
using Xunit;

namespace VarjoEye.Tests.Services;

public sealed class BaballoniaTrainerCharacterizationTests
{
    [Fact]
    public void GazePhase_AlwaysOpenLidWithoutVarjoEye()
    {
        Assert.True(BaballoniaTrainer.TryLidTarget(false, null, out var lid));
        Assert.Equal(0, lid);
    }

    [Fact]
    public void TryBegin_StartsWhenHeadsetIsReady()
    {
        var tracking = ReadyTracking();
        using var trainer = Create(tracking, new FakeBaballoniaCalibrator());

        Assert.True(trainer.TryBegin());
        Assert.True(trainer.IsBusy);
        Assert.Contains("look at the center", trainer.StatusText);
    }

    [Fact]
    public void TryBegin_RejectsWhenCalibratorIsCollecting()
    {
        var tracking = ReadyTracking();
        var calibrator = new FakeBaballoniaCalibrator { IsCollecting = true };
        using var trainer = Create(tracking, calibrator);

        Assert.False(trainer.TryBegin());
        Assert.False(trainer.IsBusy);
    }

    [Fact]
    public void TryBegin_RejectsSecondSessionWhileBusy()
    {
        var tracking = ReadyTracking();
        using var trainer = Create(tracking, new FakeBaballoniaCalibrator());

        Assert.True(trainer.TryBegin());
        Assert.False(trainer.TryBegin());
    }

    [Fact]
    public void Dispose_CancelsRunningSession()
    {
        var tracking = ReadyTracking();
        var trainer = Create(tracking, new FakeBaballoniaCalibrator());
        Assert.True(trainer.TryBegin());

        trainer.Dispose();

        Assert.False(trainer.IsBusy);
    }

    [Fact]
    public void LastLayerFit_RejectsWhenOnlyLidSamplesExist()
    {
        var stock = EmptyHead();
        var left = Enumerable.Range(0, 90).Select(_ => Sample(gazeValid: false, lid: 1)).ToArray();
        var right = Enumerable.Range(0, 90).Select(_ => Sample(gazeValid: false, lid: 1)).ToArray();

        var fitted = BaballoniaLastLayerFitter.Fit(left, right, stock, out var summary);

        Assert.Null(fitted);
        Assert.Contains("rejected", summary);
    }

    [Fact]
    public void Features_AreEnabledOnlyForAcceptedTrainingAndDisabledOnCancelOrDispose()
    {
        var tracking = ReadyTracking();
        var babble = new FakeBaballoniaTrackingService();
        using var trainer = Create(tracking, new FakeBaballoniaCalibrator(), babble);
        Assert.False(babble.TrainingFeaturesEnabled);
        Assert.True(trainer.TryBegin());
        Assert.True(babble.TrainingFeaturesEnabled);
        Assert.False(trainer.TryBegin());
        Assert.True(babble.TrainingFeaturesEnabled);
        tracking.Publish(EyeTrackingState.Empty);
        Assert.False(babble.TrainingFeaturesEnabled);
        Assert.False(trainer.IsBusy);
        trainer.Dispose();
        Assert.False(babble.TrainingFeaturesEnabled);
    }

    [Fact]
    public void RejectedTraining_DoesNotRequestFeatures()
    {
        var babble = new FakeBaballoniaTrackingService();
        using var trainer = Create(new FakeTrackingDataProvider(), new FakeBaballoniaCalibrator(), babble);
        Assert.False(trainer.TryBegin());
        Assert.False(babble.TrainingFeaturesEnabled);
    }

    private static BaballoniaTrainer Create(FakeTrackingDataProvider tracking, IBaballoniaCalibrator calibrator,
        FakeBaballoniaTrackingService? babble = null) =>
        new(
            tracking,
            babble ?? new FakeBaballoniaTrackingService(),
            new FakeCalibrationOverlay(),
            calibrator,
            new BaballoniaCalibrationStore(),
            new InMemoryLogService(), TestModelPaths.Create());

    private static FakeTrackingDataProvider ReadyTracking()
    {
        var tracking = new FakeTrackingDataProvider();
        var headset = HeadsetState.For(HeadsetCatalog.Aero, ConnectionStatus.Connected);
        tracking.Current = new EyeTrackingState(
            headset,
            CameraFrameState.Waiting(EyeSide.Left),
            CameraFrameState.Waiting(EyeSide.Right),
            EyeSourceState.Empty,
            EyeSourceState.Empty,
            true);
        return tracking;
    }

    private static BaballoniaOnnxGraph.HeadTensors EmptyHead()
    {
        var weights = new float[BaballoniaOnnxGraph.HeadOutputs * BaballoniaOnnxGraph.FeatureSize];
        var bias = new float[BaballoniaOnnxGraph.HeadOutputs];
        return new BaballoniaOnnxGraph.HeadTensors(weights, bias, weights.ToArray(), bias.ToArray());
    }

    private static BaballoniaFeatureSample Sample(bool gazeValid, double lid) =>
        new(new float[BaballoniaOnnxGraph.FeatureSize], 0.5, 0.5, lid, gazeValid, true);
}
