namespace VarjoEye.Core.Services.Baballonia;

internal static class GazeCalibrationFitter
{
    private const int MinimumSamples = 20;
    private const double MinimumAxisSpan = 0.08;
    private const double AxisRegularizationPerSample = 0.015;
    private const double AxisImproveSlack = 0.005;
    private const double AxisImproveRatio = 0.9;

    internal readonly record struct AffineAxis(double Primary, double Cross, double Bias);

    public static BaballoniaEyeCalibration? Fit(IReadOnlyList<BaballoniaCalibrationSample> samples)
    {
        if (samples.Count < MinimumSamples) return null;

        var trainCount = samples.Count * 2 / 3;
        if (trainCount < MinimumSamples || trainCount == samples.Count)
            return FitOffset(samples);

        var training = new List<BaballoniaCalibrationSample>(trainCount);
        var validation = new List<BaballoniaCalibrationSample>(samples.Count - trainCount);
        for (var i = 0; i < samples.Count; i++)
        {
            if (i < trainCount) training.Add(samples[i]);
            else validation.Add(samples[i]);
        }

        var baseline = FitOffset(training);
        if (baseline is null) return null;
        var fullOffset = FitOffset(samples);
        if (fullOffset is null) return null;

        var x = ChooseAxis(training, validation, samples, true, baseline.GazeC, fullOffset.GazeC);
        var y = ChooseAxis(training, validation, samples, false, baseline.GazeF, fullOffset.GazeF);
        return fullOffset with
        {
            GazeA = x.Primary,
            GazeB = x.Cross,
            GazeC = x.Bias,
            GazeD = y.Cross,
            GazeE = y.Primary,
            GazeF = y.Bias,
        };
    }

    internal static AffineAxis ChooseAxis(
        IReadOnlyList<BaballoniaCalibrationSample> training,
        IReadOnlyList<BaballoniaCalibrationSample> validation,
        IReadOnlyList<BaballoniaCalibrationSample> all,
        bool horizontal, double trainingOffset, double fullOffset)
    {
        var baselineError = AxisError(validation, horizontal, 1, 0, trainingOffset);
        if (TryFitAxis(training, horizontal) is not { } trained)
            return new AffineAxis(1, 0, fullOffset);
        var candidateError = AxisError(validation, horizontal, trained.Primary, trained.Cross, trained.Bias);
        if (candidateError + AxisImproveSlack >= baselineError * AxisImproveRatio)
            return new AffineAxis(1, 0, fullOffset);
        return TryFitAxis(all, horizontal) ?? trained;
    }

    private static AffineAxis? TryFitAxis(IReadOnlyList<BaballoniaCalibrationSample> samples, bool horizontal)
    {
        var n = samples.Count;
        var primary = new double[n];
        var cross = new double[n];
        var target = new double[n];
        var minPrimary = double.PositiveInfinity;
        var maxPrimary = double.NegativeInfinity;
        for (var i = 0; i < n; i++)
        {
            var sample = samples[i];
            primary[i] = horizontal ? sample.X : sample.Y;
            cross[i] = horizontal ? sample.Y : sample.X;
            target[i] = horizontal ? sample.TargetX : sample.TargetY;
            minPrimary = Math.Min(minPrimary, primary[i]);
            maxPrimary = Math.Max(maxPrimary, primary[i]);
        }

        if (maxPrimary - minPrimary < MinimumAxisSpan)
            return null;

        var meanPrimary = Mean(primary);
        var meanCross = Mean(cross);
        var meanTarget = Mean(target);

        // target ≈ primaryGain * primary + crossGain * cross + bias
        // Ridge toward identity: primaryGain ≈ 1, crossGain ≈ 0.
        var aa = 0d;
        var ab = 0d;
        var bb = 0d;
        var ay = 0d;
        var by = 0d;
        for (var i = 0; i < n; i++)
        {
            var p = primary[i] - meanPrimary;
            var c = cross[i] - meanCross;
            var t = target[i] - meanTarget;
            aa += p * p;
            ab += p * c;
            bb += c * c;
            ay += p * t;
            by += c * t;
        }

        var ridge = n * AxisRegularizationPerSample;
        aa += ridge;
        bb += ridge;
        ay += ridge;

        var determinant = aa * bb - ab * ab;
        if (determinant <= 1e-9)
            return null;

        var primaryGain = Math.Clamp((ay * bb - by * ab) / determinant, -1.5, 1.5);
        var crossGain = Math.Clamp((by * aa - ay * ab) / determinant, -1.5, 1.5);
        var bias = meanTarget - primaryGain * meanPrimary - crossGain * meanCross;
        if (!double.IsFinite(primaryGain) || !double.IsFinite(crossGain) || !double.IsFinite(bias) ||
            Math.Abs(primaryGain) + Math.Abs(crossGain) > 2 || Math.Abs(bias) > 1)
            return null;

        return new AffineAxis(primaryGain, crossGain, bias);
    }

    private static double AxisError(
        IReadOnlyList<BaballoniaCalibrationSample> samples, bool horizontal,
        double primaryGain, double crossGain, double bias)
    {
        var error = 0d;
        foreach (var sample in samples)
        {
            var primary = horizontal ? sample.X : sample.Y;
            var cross = horizontal ? sample.Y : sample.X;
            var target = horizontal ? sample.TargetX : sample.TargetY;
            error += Math.Abs(Math.Clamp(primaryGain * primary + crossGain * cross + bias, -1, 1) - target);
        }
        return error / samples.Count;
    }

    private static BaballoniaEyeCalibration? FitOffset(IReadOnlyList<BaballoniaCalibrationSample> samples)
    {
        var xOffsets = new double[samples.Count];
        var yOffsets = new double[samples.Count];
        var xBins = CreateBins();
        var yBins = CreateBins();
        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            xOffsets[i] = sample.TargetX - sample.X;
            yOffsets[i] = sample.TargetY - sample.Y;
            if (!double.IsFinite(xOffsets[i]) || !double.IsFinite(yOffsets[i]))
                return null;
            xBins[Bin(sample.TargetX)].Add(xOffsets[i]);
            yBins[Bin(sample.TargetY)].Add(yOffsets[i]);
        }

        Array.Sort(xOffsets);
        Array.Sort(yOffsets);
        return new BaballoniaEyeCalibration(
            1, 0, BalancedMedian(xBins, xOffsets),
            0, 1, BalancedMedian(yBins, yOffsets),
            1, 0);
    }

    private static List<double>[] CreateBins()
    {
        var bins = new List<double>[10];
        for (var i = 0; i < bins.Length; i++) bins[i] = new List<double>();
        return bins;
    }

    private static int Bin(double target) =>
        Math.Clamp((int)Math.Floor((target + 1) * 5), 0, 9);

    private static double BalancedMedian(List<double>[] bins, double[] allOffsets)
    {
        var medians = new List<double>();
        foreach (var bin in bins)
        {
            if (bin.Count < 10) continue;
            bin.Sort();
            medians.Add(Median(bin.ToArray()));
        }
        if (medians.Count < 2) return Median(allOffsets);
        medians.Sort();
        return Median(medians.ToArray());
    }

    private static double Median(double[] sorted)
    {
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2
            : sorted[middle];
    }

    private static double Mean(double[] values)
    {
        var sum = 0d;
        foreach (var value in values) sum += value;
        return sum / values.Length;
    }
}
