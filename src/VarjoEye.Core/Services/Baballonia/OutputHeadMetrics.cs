using VarjoEye.Core.Models;

namespace VarjoEye.Core.Services.Baballonia;

internal static class OutputHeadMetrics
{
    internal const double GazeLossWeight = 1.0;
    internal const double OpennessLossWeight = 0.6;
    internal const double BlinkOpenness = 0.25;
    internal const int MinimumBlinkEyes = 8;
    internal const double HeadErrorSlack = 0.005;
    internal const double HeadImproveRatio = 0.9;
    internal const double OpennessRange = 0.2;
    internal const double OpenLid = 0.75;
    internal const int MinimumOpennessBinCount = 8;

    internal readonly record struct EyeLidLoss(double Openness, double Blink, int BlinkCount);

    internal readonly record struct HeadLoss(double Gaze, EyeLidLoss Left, EyeLidLoss Right)
    {
        public double Openness => (Left.Openness + Right.Openness) / 2;
        public int BlinkCount => Left.BlinkCount + Right.BlinkCount;
        public double Blink => BlinkCount == 0
            ? 0
            : (Left.Blink * Left.BlinkCount + Right.Blink * Right.BlinkCount) / BlinkCount;
        public double Combined => GazeLossWeight * Gaze + OpennessLossWeight * Openness;
    }

    internal static HeadLoss MeasureHead(IReadOnlyList<BaballoniaHeadSample> samples, BaballoniaOutputHead? head)
    {
        var gaze = 0d;
        var leftOpen = 0d;
        var rightOpen = 0d;
        var leftBlink = 0d;
        var rightBlink = 0d;
        var leftBlinkCount = 0;
        var rightBlinkCount = 0;
        foreach (var sample in samples)
        {
            var values = sample.Onnx.ToFloatArray();
            head?.Apply(values);
            var eyes = BaballoniaEyeExpression.FromRaw(values);
            gaze += Math.Sqrt(Math.Pow(eyes.Left.GazeX - sample.LeftX, 2) +
                              Math.Pow(eyes.Left.GazeY - sample.LeftY, 2));
            gaze += Math.Sqrt(Math.Pow(eyes.Right.GazeX - sample.RightX, 2) +
                              Math.Pow(eyes.Right.GazeY - sample.RightY, 2));
            leftOpen += Math.Abs(eyes.Left.Openness - sample.LeftOpenness);
            rightOpen += Math.Abs(eyes.Right.Openness - sample.RightOpenness);
            if (sample.LeftOpenness < BlinkOpenness)
            {
                leftBlink += Math.Abs(eyes.Left.Openness - sample.LeftOpenness);
                leftBlinkCount++;
            }

            if (sample.RightOpenness < BlinkOpenness)
            {
                rightBlink += Math.Abs(eyes.Right.Openness - sample.RightOpenness);
                rightBlinkCount++;
            }
        }

        return new HeadLoss(
            gaze / (samples.Count * 2),
            new EyeLidLoss(leftOpen / samples.Count, leftBlinkCount == 0 ? 0 : leftBlink / leftBlinkCount, leftBlinkCount),
            new EyeLidLoss(rightOpen / samples.Count, rightBlinkCount == 0 ? 0 : rightBlink / rightBlinkCount, rightBlinkCount));
    }

    internal static bool AcceptHead(HeadLoss baseline, HeadLoss candidate)
    {
        if (candidate.Combined + HeadErrorSlack >= baseline.Combined * HeadImproveRatio)
            return false;
        if (candidate.Openness > baseline.Openness + HeadErrorSlack)
            return false;
        if (baseline.BlinkCount >= MinimumBlinkEyes &&
            candidate.Blink > baseline.Blink + HeadErrorSlack)
            return false;
        return true;
    }

    internal static bool LidImproves(HeadLoss baseline, HeadLoss candidate, bool left)
    {
        var before = left ? baseline.Left : baseline.Right;
        var after = left ? candidate.Left : candidate.Right;
        if (after.Openness + HeadErrorSlack >= before.Openness * HeadImproveRatio)
            return false;
        return before.BlinkCount < MinimumBlinkEyes ||
               after.Blink + HeadErrorSlack < before.Blink * HeadImproveRatio;
    }

    internal static bool HasOpennessCoverage(IReadOnlyList<BaballoniaHeadSample> samples, bool left)
    {
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        var closed = 0;
        var open = 0;
        foreach (var sample in samples)
        {
            var value = left ? sample.LeftOpenness : sample.RightOpenness;
            if (!double.IsFinite(value))
                continue;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
            if (value < BlinkOpenness) closed++;
            else if (value >= OpenLid) open++;
        }

        return max - min >= OpennessRange &&
               closed >= MinimumOpennessBinCount &&
               open >= MinimumOpennessBinCount;
    }

    internal static bool LidToGazeImproves(HeadLoss isolated, HeadLoss mixed) =>
        mixed.Gaze + HeadErrorSlack < isolated.Gaze * HeadImproveRatio &&
        mixed.Left.Openness <= isolated.Left.Openness + HeadErrorSlack &&
        mixed.Right.Openness <= isolated.Right.Openness + HeadErrorSlack &&
        (isolated.Left.BlinkCount < MinimumBlinkEyes || mixed.Left.Blink <= isolated.Left.Blink + HeadErrorSlack) &&
        (isolated.Right.BlinkCount < MinimumBlinkEyes || mixed.Right.Blink <= isolated.Right.Blink + HeadErrorSlack);

    internal static bool GazeToLidImproves(
        HeadLoss isolated, HeadLoss mixed, bool trainLeft, bool trainRight)
    {
        if (mixed.Gaze > isolated.Gaze + HeadErrorSlack)
            return false;
        return (trainLeft || trainRight) &&
               (!trainLeft || LidImproves(isolated, mixed, true)) &&
               (!trainRight || LidImproves(isolated, mixed, false));
    }
}
