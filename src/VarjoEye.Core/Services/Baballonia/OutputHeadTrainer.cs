using VarjoEye.Core.Models;

namespace VarjoEye.Core.Services.Baballonia;

internal readonly record struct HeadTrainingConfig(
    bool EnableLidToGaze, bool EnableGazeToLid, bool TrainLeftLid, bool TrainRightLid);

internal static class OutputHeadTrainer
{
    private const double MixingEpsilon = 1e-6;

    internal static BaballoniaOutputHead? Fit(IReadOnlyList<BaballoniaHeadSample> samples, HeadTrainingConfig config)
    {
        var weights = new double[36];
        var biases = new double[6];
        var identity = BaballoniaOutputHead.Identity;
        for (var output = 0; output < 6; output++)
        {
            if ((output == (int)EyeOutputChannel.LeftOpenness && !config.TrainLeftLid) ||
                (output == (int)EyeOutputChannel.RightOpenness && !config.TrainRightLid))
            {
                CopyChannel(identity, weights, biases, output);
                continue;
            }

            if (TryFitHeadChannel(samples, output, config) is not { } channel)
                return null;
            Array.Copy(channel.Weights, 0, weights, output * 6, 6);
            biases[output] = channel.Bias;
        }

        ApplyMixingConstraints(weights, config);
        var head = new BaballoniaOutputHead(weights, biases);
        return head.IsPlausible ? head : null;
    }

    private static (double[] Weights, double Bias)? TryFitHeadChannel(
        IReadOnlyList<BaballoniaHeadSample> samples, int output, HeadTrainingConfig config)
    {
        const int size = 7;
        var ata = new double[size, size];
        var atb = new double[size];
        foreach (var sample in samples)
        {
            var features = HeadFeatures(sample.Onnx, output, config);
            var target = HeadTarget(sample, output);
            for (var i = 0; i < size; i++)
            {
                atb[i] += features[i] * target;
                for (var j = 0; j < size; j++)
                    ata[i, j] += features[i] * features[j];
            }
        }

        var regularization = samples.Count * 0.04;
        for (var i = 0; i < size; i++) ata[i, i] += regularization;
        atb[output] += regularization;

        if (LinearSystemSolver.Solve(ata, atb) is not { } solution) return null;
        var weights = new double[6];
        for (var i = 0; i < 6; i++)
            weights[i] = Math.Clamp(solution[i], i == output ? 0.05 : -1.5, 1.5);
        var bias = Math.Clamp(solution[6], -0.7, 0.7);
        return double.IsFinite(bias) && weights.All(double.IsFinite) ? (weights, bias) : null;
    }

    private static void CopyChannel(
        BaballoniaOutputHead source, double[] weights, double[] biases, int output)
    {
        Array.Copy(source.Weights, output * 6, weights, output * 6, 6);
        biases[output] = source.Biases[output];
    }

    private static void ApplyMixingConstraints(double[] weights, HeadTrainingConfig config)
    {
        foreach (var output in EyeOutputChannels.Gaze)
        {
            if (!config.EnableLidToGaze)
            {
                foreach (var input in EyeOutputChannels.Lids)
                    weights[output * 6 + input] = 0;
            }
        }

        foreach (var output in EyeOutputChannels.Lids)
        {
            if (!config.EnableGazeToLid)
            {
                foreach (var input in EyeOutputChannels.Gaze)
                    weights[output * 6 + input] = 0;
            }
        }
    }

    private static float[] HeadFeatures(BaballoniaRawOutput onnx, int output, HeadTrainingConfig config)
    {
        var values = onnx.ToFloatArray();
        if (EyeOutputChannels.IsLid(output))
        {
            if (!config.EnableGazeToLid)
            {
                foreach (var input in EyeOutputChannels.Gaze)
                    values[input] = 0;
            }
        }
        else if (!config.EnableLidToGaze)
        {
            foreach (var input in EyeOutputChannels.Lids)
                values[input] = 0;
        }

        return [values[0], values[1], values[2], values[3], values[4], values[5], 1];
    }

    private static double HeadTarget(BaballoniaHeadSample sample, int output) => output switch
    {
        (int)EyeOutputChannel.LeftY => BaballoniaEyeExpression.ToOnnxAxis(sample.LeftY),
        (int)EyeOutputChannel.LeftX => BaballoniaEyeExpression.ToOnnxAxis(sample.LeftX),
        (int)EyeOutputChannel.RightOpenness => BaballoniaEyeExpression.ToOnnxLid(sample.RightOpenness),
        (int)EyeOutputChannel.RightY => BaballoniaEyeExpression.ToOnnxAxis(sample.RightY),
        (int)EyeOutputChannel.RightX => BaballoniaEyeExpression.ToOnnxAxis(sample.RightX),
        _ => BaballoniaEyeExpression.ToOnnxLid(sample.LeftOpenness),
    };

    internal static bool HasLidToGaze(BaballoniaOutputHead head)
    {
        foreach (var output in EyeOutputChannels.Gaze)
        {
            foreach (var input in EyeOutputChannels.Lids)
            {
                if (Math.Abs(head.Weights[output * 6 + input]) > MixingEpsilon)
                    return true;
            }
        }

        return false;
    }

    internal static bool HasGazeToLid(BaballoniaOutputHead head)
    {
        foreach (var output in EyeOutputChannels.Lids)
        {
            foreach (var input in EyeOutputChannels.Gaze)
            {
                if (Math.Abs(head.Weights[output * 6 + input]) > MixingEpsilon)
                    return true;
            }
        }

        return false;
    }

    internal static bool HasGazeLidMixing(BaballoniaOutputHead head) =>
        HasLidToGaze(head) || HasGazeToLid(head);
}
