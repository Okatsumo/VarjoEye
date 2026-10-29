namespace VarjoEye.Core.Services.Baballonia;

internal static class OutputHeadSelector
{
    internal static (HeadTrainingConfig Config, BaballoniaOutputHead Head)? Choose(
        IReadOnlyList<BaballoniaHeadSample> training,
        IReadOnlyList<BaballoniaHeadSample> validation,
        OutputHeadMetrics.HeadLoss baseline)
    {
        var probeLeft = OutputHeadMetrics.HasOpennessCoverage(training, true);
        var probeRight = OutputHeadMetrics.HasOpennessCoverage(training, false);
        var probeConfig = new HeadTrainingConfig(false, false, probeLeft, probeRight);
        if (OutputHeadTrainer.Fit(training, probeConfig) is not { } probe) return null;

        var probed = OutputHeadMetrics.MeasureHead(validation, probe);
        var trainLeft = probeLeft && OutputHeadMetrics.LidImproves(baseline, probed, true);
        var trainRight = probeRight && OutputHeadMetrics.LidImproves(baseline, probed, false);
        var isolatedConfig = new HeadTrainingConfig(false, false, trainLeft, trainRight);
        if (OutputHeadTrainer.Fit(training, isolatedConfig) is not { } isolated) return null;

        var isolatedLoss = OutputHeadMetrics.MeasureHead(validation, isolated);
        var lidToGaze = isolatedConfig with { EnableLidToGaze = true };
        var enableLidToGaze = OutputHeadTrainer.Fit(training, lidToGaze) is { } lidToGazeHead &&
            OutputHeadTrainer.HasLidToGaze(lidToGazeHead) &&
            OutputHeadMetrics.LidToGazeImproves(isolatedLoss, OutputHeadMetrics.MeasureHead(validation, lidToGazeHead));

        var gazeToLid = isolatedConfig with { EnableGazeToLid = true };
        var enableGazeToLid = (trainLeft || trainRight) &&
            OutputHeadTrainer.Fit(training, gazeToLid) is { } gazeToLidHead &&
            OutputHeadTrainer.HasGazeToLid(gazeToLidHead) &&
            OutputHeadMetrics.GazeToLidImproves(isolatedLoss, OutputHeadMetrics.MeasureHead(validation, gazeToLidHead), trainLeft, trainRight);

        var config = isolatedConfig with
        {
            EnableLidToGaze = enableLidToGaze,
            EnableGazeToLid = enableGazeToLid,
        };
        return OutputHeadTrainer.Fit(training, config) is { } trained ? (config, trained) : (isolatedConfig, isolated);
    }
}
