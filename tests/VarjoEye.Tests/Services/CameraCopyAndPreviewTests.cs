using System.Runtime.InteropServices;
using VarjoEye.Core.Models;
using VarjoEye.Core.Services.Baballonia;
using VarjoEye.Core.Services.Varjo;
using Xunit;

namespace VarjoEye.Tests.Services;

public sealed class CameraCopyAndPreviewTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void NativeCopy_PreservesRowsAndOwnsPixelsAfterNativeMemoryIsFreed(int stride)
    {
        var source = new byte[stride * 2];
        source[0] = 1; source[1] = 2; source[2] = 3;
        source[stride] = 4; source[stride + 1] = 5; source[stride + 2] = 6;
        var native = Marshal.AllocHGlobal(source.Length);
        byte[] copied;
        try
        {
            Marshal.Copy(source, 0, native, source.Length);
            copied = VarjoRuntime.CopyCameraPixels(native, 3, 2, stride);
        }
        finally { Marshal.FreeHGlobal(native); }
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, copied);
    }

    [Fact]
    public void Preview_CanBeDisabledWithoutStoppingInferenceAndKeepsOwnTimestampAndPixels()
    {
        using var inference = new BaballoniaEyeInference(TestModelPaths.Create().StockPath, InferenceDevice.Cpu);
        var window = new List<CameraEyeFramePair>();
        for (var i = 1; i <= 4; i++) Run(i, false);
        Assert.Null(inference.CurrentInputFrame);
        Run(5, true);
        var preview = inference.CurrentInputFrame!;
        Assert.NotNull(preview);
        Assert.Equal(25_000_000, preview.CaptureTimeNanoseconds);
        Assert.Equal(5, preview.FrameNumber);
        var saved = preview.Left.ToArray();
        Run(6, false);
        Assert.Null(inference.CurrentInputFrame);
        Assert.Equal(saved, preview.Left);
        Run(7, true);
        Assert.Equal(35_000_000, inference.CurrentInputFrame!.CaptureTimeNanoseconds);

        void Run(int number, bool previewEnabled)
        {
            var pixels = new byte[64];
            var timestamp = number * 5_000_000L;
            window.Add(new(number, DateTimeOffset.UtcNow,
                new(true, 8, 8, pixels, number, timestamp), new(false, 8, 8, pixels, number, timestamp)));
            if (window.Count > 4) window.RemoveAt(0);
            var result = inference.Run(window, false, generatePreview: previewEnabled);
            if (number >= 4) Assert.NotNull(result);
        }
    }
}
