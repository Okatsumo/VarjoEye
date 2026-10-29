using VarjoEye.Core.Models;
using VarjoEye.Core.Services.Baballonia;
using Xunit;

namespace VarjoEye.Tests.Services;

public sealed class BaballoniaFeatureModeTests
{
    [Fact]
    public void FeatureMode_SwitchesWithoutChangingGazeAndPreservesPublishedFeatures()
    {
        using var inference = new BaballoniaEyeInference(TestModelPaths.Create().StockPath, InferenceDevice.Cpu);
        var window = new List<CameraEyeFramePair>();
        for (var i = 1; i <= 3; i++) Run(i, false);
        var normal = Run(4, false)!;
        Assert.Null(normal.LeftFeatures);
        Assert.Null(normal.RightFeatures);
        var training = Run(5, true)!;
        Assert.Equal(BaballoniaOnnxGraph.FeatureSize, training.LeftFeatures!.Length);
        Assert.Equal(BaballoniaOnnxGraph.FeatureSize, training.RightFeatures!.Length);
        Assert.Equal(normal.RawOutput, training.RawOutput);
        var saved = training.LeftFeatures.ToArray();
        var after = Run(6, false)!;
        Assert.Null(after.LeftFeatures);
        Assert.Null(after.RightFeatures);
        Assert.Equal(normal.RawOutput, after.RawOutput);
        Assert.Equal(saved, training.LeftFeatures);
        var again = Run(7, true)!;
        Assert.NotSame(training.LeftFeatures, again.LeftFeatures);
        Assert.Equal(saved, again.LeftFeatures);

        BaballoniaEyeResult? Run(int number, bool features)
        {
            var pixels = new byte[64];
            var timestamp = number * 5_000_000L;
            window.Add(new(number, DateTimeOffset.UtcNow,
                new(true, 8, 8, pixels, number, timestamp), new(false, 8, 8, pixels, number, timestamp)));
            if (window.Count > 4) window.RemoveAt(0);
            return inference.Run(window, false, generatePreview: false, captureFeatures: features);
        }
    }
}
