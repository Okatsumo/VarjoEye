using VarjoEye.Core.Models;
using VarjoEye.Core.Services.Baballonia;
using Xunit;

namespace VarjoEye.Tests.Services;

public sealed class BaballoniaCalibrationFitterTests
{
    [Fact]
    public void Fit_ReturnsNullWhenTooFewSamples()
    {
        var samples = new List<BaballoniaCalibrationSample>();
        for (var i = 0; i < 19; i++)
            samples.Add(new(0, 0, 1, 0, 0, 1));

        Assert.Null(BaballoniaCalibrationFitter.Fit(samples));
    }

    [Fact]
    public void Fit_KeepsIdentityWhenBabbleAlreadyMatchesVarjo()
    {
        var samples = new List<BaballoniaCalibrationSample>();
        for (var i = 0; i < 30; i++)
        {
            var x = (i % 10) / 5.0 - 1;
            var y = (i / 10) / 5.0 - 0.3;
            samples.Add(new(x, y, 1, x, y, 1));
        }

        var fitted = BaballoniaCalibrationFitter.Fit(samples);

        Assert.NotNull(fitted);
        Assert.Equal(1, fitted.GazeA);
        Assert.Equal(0, fitted.GazeB);
        Assert.Equal(0, fitted.GazeC, 3);
        Assert.Equal(0, fitted.GazeD);
        Assert.Equal(1, fitted.GazeE);
        Assert.Equal(0, fitted.GazeF, 3);
        Assert.True(fitted.IsIdentity);
    }

    [Fact]
    public void Fit_RecoversConstantGazeOffset()
    {
        var samples = new List<BaballoniaCalibrationSample>();
        for (var i = 0; i < 30; i++)
        {
            var x = (i % 10) / 5.0 - 1;
            var y = (i / 10) / 5.0 - 0.3;
            samples.Add(new(x, y, 1, x + 0.12, y - 0.08, 1));
        }

        var fitted = BaballoniaCalibrationFitter.Fit(samples);

        Assert.NotNull(fitted);
        Assert.Equal(1, fitted.GazeA);
        Assert.Equal(0, fitted.GazeB);
        Assert.Equal(0.12, fitted.GazeC, 3);
        Assert.Equal(0, fitted.GazeD);
        Assert.Equal(1, fitted.GazeE);
        Assert.Equal(-0.08, fitted.GazeF, 3);
    }

    [Fact]
    public void Identity_MapsUnchangedAndStaysPlausibleAtPipeline9()
    {
        var raw = new BaballoniaEye(0.25, -0.4, 0.7);
        var mapped = BaballoniaEyeCalibration.Identity.Map(raw);
        var calibration = new BaballoniaCalibration(
            BaballoniaEyeCalibration.Identity,
            BaballoniaEyeCalibration.Identity,
            20,
            0,
            BaballoniaCalibration.CurrentPipelineVersion);

        Assert.Equal(raw, mapped);
        Assert.Equal(9, BaballoniaCalibration.CurrentPipelineVersion);
        Assert.True(calibration.IsPlausible);
        Assert.True(BaballoniaOutputHead.Identity.IsPlausible);
    }

    [Fact]
    public void ChooseAxis_AcceptsOnTrainingThenRefitsAllWithoutValidationRescore()
    {
        var training = ScaleX(24, 0.5);
        var validation = ScaleX(12, 0.5);
        var extra = ScaleX(36, 0.95);
        var all = training.Concat(validation).Concat(extra).ToList();

        var chosen = GazeCalibrationFitter.ChooseAxis(training, validation, all, true, 0, 0);

        Assert.NotEqual(1, chosen.Primary);
        Assert.Equal(0, chosen.Cross, 3);
    }

    [Fact]
    public void ApplyHead_LeavesSamplesWithoutOnnxUnchanged()
    {
        var sample = new BaballoniaCalibrationSample(0.1, 0.2, 0.9, 0.3, 0.4, 0.8);
        var mapped = BaballoniaCalibrationFitter.ApplyHead([sample], BaballoniaOutputHead.Identity, true);

        Assert.Equal(sample, mapped[0]);
    }

    [Fact]
    public void AcceptHead_RejectsWhenOpennessGetsWorse()
    {
        var baseline = Loss(0.20, 0.05, 0.04, 12);
        var candidate = Loss(0.10, 0.12, 0.04, 12);

        Assert.False(OutputHeadMetrics.AcceptHead(baseline, candidate));
    }

    [Fact]
    public void AcceptHead_RejectsWhenBlinkGetsWorse()
    {
        var baseline = Loss(0.20, 0.08, 0.05, 12);
        var candidate = Loss(0.10, 0.07, 0.18, 12);

        Assert.False(OutputHeadMetrics.AcceptHead(baseline, candidate));
    }

    [Fact]
    public void AcceptHead_IgnoresBlinkGateWithoutClosedEyes()
    {
        var baseline = Loss(0.20, 0.08, 0, 0);
        var candidate = Loss(0.10, 0.07, 0, 0);

        Assert.True(OutputHeadMetrics.AcceptHead(baseline, candidate));
    }

    [Fact]
    public void LidToGazeImproves_RequiresGazeGainWithoutLidRegression()
    {
        var isolated = Loss(0.20, 0.08, 0.06, 12);
        var betterGaze = Loss(0.10, 0.08, 0.06, 12);
        var worseLids = Loss(0.10, 0.12, 0.06, 12);

        Assert.True(OutputHeadMetrics.LidToGazeImproves(isolated, betterGaze));
        Assert.False(OutputHeadMetrics.LidToGazeImproves(isolated, worseLids));
    }

    [Fact]
    public void GazeToLidImproves_DoesNotRequireGazeGain()
    {
        var isolated = Loss(0.20, 0.20, 0.18, 12);
        var betterLids = Loss(0.20, 0.08, 0.05, 12);

        Assert.True(OutputHeadMetrics.GazeToLidImproves(isolated, betterLids, true, true));
        Assert.False(OutputHeadMetrics.LidToGazeImproves(isolated, betterLids));
    }

    [Fact]
    public void HasGazeLidMixing_UsesConstrainedWeights()
    {
        var weights = (double[])BaballoniaOutputHead.Identity.Weights.Clone();
        weights[0 * 6 + 2] = 0.4;
        weights[2 * 6 + 1] = 0.3;
        var head = new BaballoniaOutputHead(weights, new double[6]);

        Assert.True(OutputHeadTrainer.HasLidToGaze(head));
        Assert.True(OutputHeadTrainer.HasGazeToLid(head));
        weights[0 * 6 + 2] = 0;
        head = new BaballoniaOutputHead(weights, new double[6]);
        Assert.False(OutputHeadTrainer.HasLidToGaze(head));
        Assert.True(OutputHeadTrainer.HasGazeToLid(head));
    }

    [Fact]
    public void LidImproves_KeepsTheBetterEyeWhenTheOtherWorsens()
    {
        var baseline = new OutputHeadMetrics.HeadLoss(
            0.20,
            new OutputHeadMetrics.EyeLidLoss(0.20, 0.18, 12),
            new OutputHeadMetrics.EyeLidLoss(0.08, 0.06, 12));
        var candidate = new OutputHeadMetrics.HeadLoss(
            0.10,
            new OutputHeadMetrics.EyeLidLoss(0.08, 0.05, 12),
            new OutputHeadMetrics.EyeLidLoss(0.12, 0.11, 12));

        Assert.True(OutputHeadMetrics.LidImproves(baseline, candidate, true));
        Assert.False(OutputHeadMetrics.LidImproves(baseline, candidate, false));
    }

    [Fact]
    public void FitOutputHead_FreezesLidsWhenEyesStayOpen()
    {
        var samples = ShiftedGaze(30, openness: 1);
        var head = BaballoniaCalibrationFitter.FitOutputHead(samples);

        Assert.NotNull(head);
        AssertIdentityLid(head);
        Assert.False(OutputHeadTrainer.HasGazeLidMixing(head));
    }

    [Fact]
    public void FitOutputHead_DoesNotLetLidsDriveGazeWhenEyesStayOpen()
    {
        var samples = ShiftedGaze(30, openness: 1);
        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            var onnx = sample.Onnx;
            samples[i] = sample with
            {
                Onnx = onnx with
                {
                    Value2 = i % 2 == 0 ? 0.05f : 0.95f,
                    Value5 = i % 2 == 0 ? 0.95f : 0.05f
                }
            };
        }

        var head = BaballoniaCalibrationFitter.FitOutputHead(samples);

        Assert.NotNull(head);
        Assert.False(OutputHeadTrainer.HasGazeLidMixing(head));
    }

    [Fact]
    public void FitOutputHead_KeepsLidFreezeWhenOnlyFullSetHasBlinks()
    {
        var samples = ShiftedGaze(48, openness: 0.95);
        for (var i = 32; i < 40; i++)
        {
            var sample = samples[i];
            samples[i] = Sample(sample.LeftX, sample.LeftY, 0.1, sample.RightX, sample.RightY, 0.1, gazeOffset: 0.18);
        }

        var head = BaballoniaCalibrationFitter.FitOutputHead(samples);

        Assert.NotNull(head);
        AssertIdentityLid(head);
        Assert.False(OutputHeadTrainer.HasGazeLidMixing(head));
    }

    [Fact]
    public void HasOpennessCoverage_DoesNotCombineEyes()
    {
        var samples = new List<BaballoniaHeadSample>();
        for (var i = 0; i < 20; i++)
            samples.Add(Sample(0, 0, 0.9, 0, 0, 0.5));

        Assert.False(OutputHeadMetrics.HasOpennessCoverage(samples, true));
        Assert.False(OutputHeadMetrics.HasOpennessCoverage(samples, false));
    }

    [Fact]
    public void HasOpennessCoverage_RejectsASingleBlinkOutlier()
    {
        var samples = ShiftedGaze(20, openness: 0.95);
        samples[0] = Sample(0, 0, 0.05, 0, 0, 0.05);

        Assert.False(OutputHeadMetrics.HasOpennessCoverage(samples, true));
        Assert.False(OutputHeadMetrics.HasOpennessCoverage(samples, false));
    }

    [Fact]
    public void HasOpennessCoverage_RequiresClosedAndOpenBinsPerEye()
    {
        var samples = new List<BaballoniaHeadSample>();
        for (var i = 0; i < 8; i++)
            samples.Add(Sample(0, 0, 0.1, 0, 0, 0.95));
        for (var i = 0; i < 8; i++)
            samples.Add(Sample(0, 0, 0.95, 0, 0, 0.95));

        Assert.True(OutputHeadMetrics.HasOpennessCoverage(samples, true));
        Assert.False(OutputHeadMetrics.HasOpennessCoverage(samples, false));
    }

    [Fact]
    public void MeasureHead_SeparatesGazeOpennessAndBlink()
    {
        var samples = new List<BaballoniaHeadSample>
        {
            Sample(0.2, 0, 1, 0.2, 0, 1),
            Sample(0, 0, 0.1, 0, 0, 0.1, gazeOffset: 0.4, lidOffset: 0.2),
        };

        var loss = OutputHeadMetrics.MeasureHead(samples, null);

        Assert.Equal(0.2, loss.Gaze, 3);
        Assert.Equal(0.05, loss.Left.Openness, 3);
        Assert.Equal(0.05, loss.Right.Openness, 3);
        Assert.Equal(0.1, loss.Left.Blink, 3);
        Assert.Equal(1, loss.Left.BlinkCount);
        Assert.Equal(1, loss.Right.BlinkCount);
    }

    private static OutputHeadMetrics.HeadLoss Loss(
        double gaze, double openness, double blink, int blinkCount) =>
        new(gaze,
            new OutputHeadMetrics.EyeLidLoss(openness, blink, blinkCount),
            new OutputHeadMetrics.EyeLidLoss(openness, blink, blinkCount));

    private static void AssertIdentityLid(BaballoniaOutputHead head)
    {
        var identity = BaballoniaOutputHead.Identity;
        foreach (var output in new[] { 2, 5 })
        {
            for (var i = 0; i < 6; i++)
                Assert.Equal(identity.Weights[output * 6 + i], head.Weights[output * 6 + i]);
            Assert.Equal(identity.Biases[output], head.Biases[output]);
        }
    }

    private static List<BaballoniaCalibrationSample> ScaleX(int count, double gain)
    {
        var samples = new List<BaballoniaCalibrationSample>(count);
        for (var i = 0; i < count; i++)
        {
            var x = i / (double)Math.Max(count - 1, 1) * 1.6 - 0.8;
            samples.Add(new(x, 0, 1, x * gain, 0, 1));
        }

        return samples;
    }

    private static List<BaballoniaHeadSample> ShiftedGaze(int count, double openness)
    {
        var samples = new List<BaballoniaHeadSample>(count);
        for (var i = 0; i < count; i++)
        {
            var x = (i % 10) / 5.0 - 1;
            var y = (i / 10) / 5.0 - 0.3;
            samples.Add(Sample(x, y, openness, x, y, openness, gazeOffset: 0.18));
        }

        return samples;
    }

    private static BaballoniaHeadSample Sample(
        double leftX, double leftY, double leftOpen,
        double rightX, double rightY, double rightOpen,
        double gazeOffset = 0, double lidOffset = 0)
    {
        var onnx = new BaballoniaRawOutput(
            (float)BaballoniaEyeExpression.ToOnnxAxis(leftY),
            (float)BaballoniaEyeExpression.ToOnnxAxis(leftX + gazeOffset),
            (float)(BaballoniaEyeExpression.ToOnnxLid(leftOpen) + lidOffset),
            (float)BaballoniaEyeExpression.ToOnnxAxis(rightY),
            (float)BaballoniaEyeExpression.ToOnnxAxis(rightX + gazeOffset),
            (float)(BaballoniaEyeExpression.ToOnnxLid(rightOpen) + lidOffset));
        return new BaballoniaHeadSample(onnx, leftX, leftY, leftOpen, rightX, rightY, rightOpen);
    }
}
