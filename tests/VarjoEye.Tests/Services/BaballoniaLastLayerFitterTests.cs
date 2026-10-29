using VarjoEye.Core.Services;
using VarjoEye.Core.Services.Baballonia;
using Xunit;

namespace VarjoEye.Tests.Services;

public sealed class BaballoniaLastLayerFitterTests
{
    [Fact]
    public void Fit_RejectsTooFewSamples()
    {
        var stock = EmptyHead();
        var fitted = BaballoniaLastLayerFitter.Fit(
            [Sample(gazeValid: true)],
            [Sample(gazeValid: true)],
            stock,
            out var summary);
        Assert.Null(fitted);
        Assert.Contains("rejected", summary);
    }

    [Fact]
    public void Trainer_RequiresCalibratedHeadset()
    {
        var frames = new Fakes.FakeTrackingDataProvider();
        var overlay = new Fakes.FakeCalibrationOverlay();
        using var trainer = new BaballoniaTrainer(
            frames,
            new Fakes.FakeBaballoniaTrackingService(),
            overlay,
            new Fakes.FakeBaballoniaCalibrator(),
            new BaballoniaCalibrationStore(),
            new InMemoryLogService(), TestModelPaths.Create());
        Assert.False(trainer.TryBegin());
        Assert.False(trainer.IsBusy);
    }

    private static BaballoniaOnnxGraph.HeadTensors EmptyHead()
    {
        var weights = new float[BaballoniaOnnxGraph.HeadOutputs * BaballoniaOnnxGraph.FeatureSize];
        var bias = new float[BaballoniaOnnxGraph.HeadOutputs];
        return new BaballoniaOnnxGraph.HeadTensors(weights, bias, weights.ToArray(), bias.ToArray());
    }

    private static BaballoniaFeatureSample Sample(bool gazeValid) =>
        new(new float[BaballoniaOnnxGraph.FeatureSize], 0.5, 0.5, 0, gazeValid, true);
}
