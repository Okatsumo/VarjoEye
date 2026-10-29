using System;
using System.Collections.Generic;

namespace VarjoEye.Core.Services.Baballonia;

public readonly record struct BaballoniaFeatureSample(
    float[] Features,
    double GazeY,
    double GazeX,
    double Lid,
    bool GazeValid,
    bool LidValid);

public static class BaballoniaLastLayerFitter
{
    private const int FeatureSize = BaballoniaOnnxGraph.FeatureSize;
    private const int Outputs = BaballoniaOnnxGraph.HeadOutputs;
    private const int MinimumGaze = 80;
    private const int MinimumLidClass = 40;
    private const int Size = FeatureSize + 1;

    public static BaballoniaOnnxGraph.HeadTensors? Fit(
        IReadOnlyList<BaballoniaFeatureSample> left,
        IReadOnlyList<BaballoniaFeatureSample> right,
        BaballoniaOnnxGraph.HeadTensors stock,
        out string summary)
    {
        var leftFitted = FitEye(left, stock.LeftWeight, stock.LeftBias, out var leftError);
        var rightFitted = FitEye(right, stock.RightWeight, stock.RightBias, out var rightError);
        if (leftFitted is null || rightFitted is null)
        {
            summary = $"rejected (left={left.Count}, right={right.Count})";
            return null;
        }

        summary = $"left {leftError.Stock:0.000} → {leftError.Fitted:0.000}, " +
                  $"right {rightError.Stock:0.000} → {rightError.Fitted:0.000}";
        return new BaballoniaOnnxGraph.HeadTensors(
            leftFitted.Value.Weights, leftFitted.Value.Biases,
            rightFitted.Value.Weights, rightFitted.Value.Biases);
    }

    private const double MinimumGazeImprovement = 0.08;
    private const double GazeErrorTolerance = 0.004;

    private static (float[] Weights, float[] Biases)? FitEye(
        IReadOnlyList<BaballoniaFeatureSample> samples,
        float[] stockWeights,
        float[] stockBiases,
        out (double Stock, double Fitted) error)
    {
        error = default;

        if (samples.Count(sample => sample.GazeValid) < MinimumGaze)
            return null;

        var trainingSamples = new List<BaballoniaFeatureSample>();
        var validationSamples = new List<BaballoniaFeatureSample>();

        for (var i = 0; i < samples.Count; i++)
        {
            var target = i % 3 == 2
                ? validationSamples
                : trainingSamples;

            target.Add(samples[i]);
        }

        if (trainingSamples.Count < MinimumGaze ||
            validationSamples.Count < 20)
            return null;

        // Validate the model on samples excluded from training.
        if (TryFit(trainingSamples, stockWeights, stockBiases) is not { } trained)
            return null;

        var stockError = GazeError(validationSamples, stockWeights, stockBiases);
        var trainedError = GazeError(validationSamples, trained.Weights, trained.Biases);

        if (!HasSufficientImprovement(stockError, trainedError))
            return null;

        // Refit using the complete dataset.
        if (TryFit(samples, stockWeights, stockBiases) is not { } fitted)
            return null;

        var fittedError = GazeError(validationSamples, fitted.Weights, fitted.Biases);

        if (!HasSufficientImprovement(stockError, fittedError))
            return null;

        // Preserve stock eyelid parameters if calibration does not improve them.
        if (!LidImproves(
                validationSamples,
                stockWeights,
                stockBiases,
                fitted.Weights,
                fitted.Biases))
        {
            CopyChannel(stockWeights, stockBiases, fitted.Weights, fitted.Biases, 2);
        }

        error = (stockError, fittedError);
        return fitted;
    }

    private static bool HasSufficientImprovement(
        double baselineError,
        double candidateError)
    {
        return candidateError + GazeErrorTolerance
            < baselineError * (1.0 - MinimumGazeImprovement);
    }

    private static (float[] Weights, float[] Biases)? TryFit(
        IReadOnlyList<BaballoniaFeatureSample> samples,
        float[] stockWeights, float[] stockBiases)
    {
        var weights = (float[])stockWeights.Clone();
        var biases = (float[])stockBiases.Clone();
        var fittedGaze = false;
        for (var output = 0; output < Outputs; output++)
        {
            if (TryFitChannel(samples, output, stockWeights, stockBiases) is not { } channel)
                continue;
            Array.Copy(channel.Weights, 0, weights, output * FeatureSize, FeatureSize);
            biases[output] = channel.Bias;
            if (output < 2) fittedGaze = true;
        }
        return fittedGaze ? (weights, biases) : null;
    }

    private static (float[] Weights, float Bias)? TryFitChannel(
        IReadOnlyList<BaballoniaFeatureSample> samples, int output,
        float[] stockWeights, float[] stockBiases)
    {
        IReadOnlyList<BaballoniaFeatureSample> usedSamples = output == 2
            ? BalanceLid(samples) : samples;
        var ata = new double[Size, Size];
        var atb = new double[Size];
        var used = 0;
        foreach (var sample in usedSamples)
        {
            if (output < 2 && !sample.GazeValid) continue;
            if (output == 2 && !sample.LidValid) continue;
            var target = Logit(Target(sample, output));
            for (var i = 0; i < FeatureSize; i++)
            {
                var feature = sample.Features[i];
                atb[i] += feature * target;
                for (var j = 0; j < FeatureSize; j++)
                    ata[i, j] += feature * sample.Features[j];
                ata[i, FeatureSize] += feature;
                ata[FeatureSize, i] += feature;
            }
            ata[FeatureSize, FeatureSize] += 1;
            atb[FeatureSize] += target;
            used++;
        }
        if (used < (output == 2 ? MinimumLidClass * 2 : MinimumGaze)) return null;

        var regularization = output == 2
            ? Math.Max(400, used * 0.45)
            : Math.Max(80, used * 0.05);
        for (var i = 0; i < FeatureSize; i++)
        {
            ata[i, i] += regularization;
            atb[i] += regularization * stockWeights[output * FeatureSize + i];
        }
        ata[FeatureSize, FeatureSize] += regularization;
        atb[FeatureSize] += regularization * stockBiases[output];

        if (Solve(ata, atb) is not { } solution) return null;
        var weights = new float[FeatureSize];
        for (var i = 0; i < FeatureSize; i++)
        {
            if (!double.IsFinite(solution[i])) return null;
            weights[i] = (float)solution[i];
        }
        if (!double.IsFinite(solution[FeatureSize])) return null;
        return (weights, (float)solution[FeatureSize]);
    }

    private static List<BaballoniaFeatureSample> BalanceLid(
        IReadOnlyList<BaballoniaFeatureSample> samples)
    {
        var open = new List<BaballoniaFeatureSample>();
        var closed = new List<BaballoniaFeatureSample>();
        foreach (var sample in samples)
        {
            if (!sample.LidValid) continue;
            if (sample.Lid >= 0.5) closed.Add(sample);
            else open.Add(sample);
        }
        if (closed.Count < MinimumLidClass || open.Count < MinimumLidClass)
            return [];
        var cap = Math.Min(open.Count, closed.Count * 2);
        if (open.Count > cap)
        {
            var step = (double)open.Count / cap;
            var trimmed = new List<BaballoniaFeatureSample>(cap + closed.Count);
            for (var i = 0; i < cap; i++)
                trimmed.Add(open[(int)(i * step)]);
            trimmed.AddRange(closed);
            return trimmed;
        }
        var all = new List<BaballoniaFeatureSample>(open.Count + closed.Count);
        all.AddRange(open);
        all.AddRange(closed);
        return all;
    }

    private static bool LidImproves(
        IReadOnlyList<BaballoniaFeatureSample> samples,
        float[] stockWeights, float[] stockBiases,
        float[] fittedWeights, float[] fittedBiases)
    {
        var stock = ChannelError(samples, 2, stockWeights, stockBiases);
        var fitted = ChannelError(samples, 2, fittedWeights, fittedBiases);
        return fitted + 0.01 < stock * 0.9;
    }

    private static void CopyChannel(
        float[] fromWeights, float[] fromBiases, float[] toWeights, float[] toBiases, int output)
    {
        Array.Copy(fromWeights, output * FeatureSize, toWeights, output * FeatureSize, FeatureSize);
        toBiases[output] = fromBiases[output];
    }

    private static double GazeError(
        IReadOnlyList<BaballoniaFeatureSample> samples, float[] weights, float[] biases) =>
        (ChannelError(samples, 0, weights, biases) + ChannelError(samples, 1, weights, biases)) / 2;

    private static double ChannelError(
        IReadOnlyList<BaballoniaFeatureSample> samples, int output, float[] weights, float[] biases)
    {
        var error = 0d;
        var count = 0;
        foreach (var sample in samples)
        {
            if (output < 2 && !sample.GazeValid) continue;
            if (output == 2 && !sample.LidValid) continue;
            var sum = biases[output];
            var offset = output * FeatureSize;
            for (var i = 0; i < FeatureSize; i++)
                sum += weights[offset + i] * sample.Features[i];
            error += Math.Abs(Sigmoid(sum) - Target(sample, output));
            count++;
        }
        return count == 0 ? 1 : error / count;
    }

    private static double Target(BaballoniaFeatureSample sample, int output) => output switch
    {
        0 => sample.GazeY,
        1 => sample.GazeX,
        _ => sample.Lid,
    };

    private static double Logit(double value)
    {
        var p = Math.Clamp(value, 1e-4, 1 - 1e-4);
        return Math.Log(p / (1 - p));
    }

    private static double Sigmoid(double value) => 1 / (1 + Math.Exp(-Math.Clamp(value, -20, 20)));

    private static double[]? Solve(double[,] matrix, double[] values)
    {
        var n = values.Length;
        var a = new double[n, n + 1];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++) a[i, j] = matrix[i, j];
            a[i, n] = values[i];
        }
        for (var k = 0; k < n; k++)
        {
            var pivot = k;
            for (var i = k + 1; i < n; i++)
                if (Math.Abs(a[i, k]) > Math.Abs(a[pivot, k])) pivot = i;
            if (Math.Abs(a[pivot, k]) < 1e-9) return null;
            if (pivot != k)
            {
                for (var j = k; j <= n; j++)
                    (a[k, j], a[pivot, j]) = (a[pivot, j], a[k, j]);
            }
            var diagonal = a[k, k];
            for (var j = k; j <= n; j++) a[k, j] /= diagonal;
            for (var i = 0; i < n; i++)
            {
                if (i == k) continue;
                var factor = a[i, k];
                for (var j = k; j <= n; j++) a[i, j] -= factor * a[k, j];
            }
        }
        var solution = new double[n];
        for (var i = 0; i < n; i++)
        {
            if (!double.IsFinite(a[i, n])) return null;
            solution[i] = a[i, n];
        }
        return solution;
    }
}
