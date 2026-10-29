using VarjoEye.Core.Models;

namespace VarjoEye.Core.Services.Baballonia;

public static class BaballoniaCalibrationFitter
{
    private const int MinimumSamples = 20;

    public static BaballoniaEyeCalibration? Fit(IReadOnlyList<BaballoniaCalibrationSample> samples) =>
        GazeCalibrationFitter.Fit(samples);

    public static BaballoniaOutputHead? FitOutputHead(IReadOnlyList<BaballoniaHeadSample> samples)
    {
        if (samples.Count < MinimumSamples) return null;
        var trainCount = samples.Count * 2 / 3;
        if (trainCount < MinimumSamples || trainCount == samples.Count) return null;

        var training = new List<BaballoniaHeadSample>(trainCount);
        var validation = new List<BaballoniaHeadSample>(samples.Count - trainCount);
        for (var i = 0; i < samples.Count; i++)
        {
            if (i < trainCount) training.Add(samples[i]);
            else validation.Add(samples[i]);
        }

        var baseline = OutputHeadMetrics.MeasureHead(validation, null);
        if (OutputHeadSelector.Choose(training, validation, baseline) is not { } chosen) return null;
        if (!OutputHeadMetrics.AcceptHead(baseline, OutputHeadMetrics.MeasureHead(validation, chosen.Head))) return null;
        return OutputHeadTrainer.Fit(samples, chosen.Config);
    }

    public static List<BaballoniaCalibrationSample> ApplyHead(
        IReadOnlyList<BaballoniaCalibrationSample> samples, BaballoniaOutputHead head, bool left)
    {
        var mapped = new List<BaballoniaCalibrationSample>(samples.Count);
        foreach (var sample in samples)
        {
            if (sample.Onnx is not { } onnx)
            {
                mapped.Add(sample);
                continue;
            }
            var values = onnx.ToFloatArray();
            head.Apply(values);
            var eyes = BaballoniaEyeExpression.FromRaw(values);
            var eye = left ? eyes.Left : eyes.Right;
            mapped.Add(sample with { X = eye.GazeX, Y = eye.GazeY, Openness = eye.Openness });
        }
        return mapped;
    }
}
