using VarjoEye.Core.Models;
using VarjoEye.Core.Services.Baballonia;
using Xunit;

namespace VarjoEye.Tests.Services;

public sealed class BaballoniaFramePreprocessorTests
{
    [Fact]
    public void Prepare_ProducesNormalized128Channel()
    {
        var pixels = new byte[16];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i * 16);
        }

        var prepared = BaballoniaFramePreprocessor.Prepare(new EyeCameraFrame(true, 4, 4, pixels, 1, 10), null);
        Assert.Equal(128 * 128, prepared.Length);
        Assert.Contains(prepared, value => value == 0);
        Assert.Contains(prepared, value => value > 0.5f);
        Assert.All(prepared, value => Assert.InRange(value, 0f, 1f));
    }

    [Fact]
    public void Prepare_InvalidFrame_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            BaballoniaFramePreprocessor.Prepare(new EyeCameraFrame(true, 2, 2, new byte[1], 1), null));
    }

    [Fact]
    public void ToPreviewBytes_ScalesToByteRange()
    {
        var preview = BaballoniaFramePreprocessor.ToPreviewBytes([0f, 0.5f, 1f]);
        Assert.Equal([0, 128, 255], preview);
    }
}
