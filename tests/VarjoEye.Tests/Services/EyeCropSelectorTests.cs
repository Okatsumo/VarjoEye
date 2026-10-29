using VarjoEye.Core.Models;
using VarjoEye.Core.Services.Baballonia;
using Xunit;

namespace VarjoEye.Tests.Services;

public sealed class EyeCropSelectorTests
{
    [Theory]
    [InlineData(true, 360)]
    [InlineData(false, 260)]
    public void NeutralGaze_SelectsPupilInsteadOfDarkLensEdge(bool left, int pupilX)
    {
        var selector = new EyeCropSelector();
        var pixels = Image(pupilX);
        for (var i = 1; i <= 24; i++)
            selector.Observe(new(left, 640, 400, pixels, i, i * 5_000_000L), Gaze(i));
        Assert.NotNull(selector.Crop);
        var crop = selector.Crop!.Value;
        Assert.Equal(352, crop.Size);
        Assert.InRange(crop.X + crop.Size / 2 - pupilX, -24, 24);
        Assert.InRange(crop.Y + crop.Size / 2 - 160, -24, 24);
        var fixedCrop = selector.Crop;
        selector.Observe(new(left, 640, 400, Image(pupilX + 80), 25, 125_000_000), Gaze(25, 0.4));
        Assert.Equal(fixedCrop, selector.Crop);
    }

    [Fact]
    public void Restart_DropsOldCropUntilNewNeutralObservations()
    {
        var selector = new EyeCropSelector();
        var pixels = Image(360);
        for (var i = 1; i <= 24; i++) selector.Observe(new(true, 640, 400, pixels, i, i * 5_000_000L), Gaze(i));
        Assert.NotNull(selector.Crop);
        selector.Observe(new(true, 640, 400, pixels, 1, 5_000_000L));
        Assert.Null(selector.Crop);
    }

    [Theory]
    [InlineData(0.4, 1, 0)]
    [InlineData(0, 0.1, 0)]
    [InlineData(0, 1, 50_000_000)]
    public void UnusableGaze_DoesNotLockCrop(double gazeX, double openness, long age)
    {
        var selector = new EyeCropSelector();
        var pixels = Image(360);
        for (var i = 1; i <= 30; i++)
        {
            var gaze = Gaze(i, gazeX, openness);
            selector.Observe(new(true, 640, 400, pixels, i, i * 5_000_000L + age), gaze);
        }
        Assert.Null(selector.Crop);
    }

    [Fact]
    public void LensEdgeAlone_AndMissingGaze_DoNotLockCrop()
    {
        var selector = new EyeCropSelector();
        var edge = Image(-100);
        for (var i = 1; i <= 30; i++)
            selector.Observe(new(true, 640, 400, edge, i, i * 5_000_000L), Gaze(i));
        Assert.Null(selector.Crop);
        var noGaze = new EyeCropSelector();
        for (var i = 1; i <= 30; i++) noGaze.Observe(new(true, 640, 400, Image(360), i, i * 5_000_000L));
        Assert.Null(noGaze.Crop);
    }

    private static GazeSample Gaze(int frame, double x = 0, double openness = 1)
    {
        var eye = new EyeTrackingEye(openness, 5, 12, 0.4, 0.5, Vector3Value.Zero, new(x, 0, 1));
        return new(frame, eye, eye, frame * 5_000_000L);
    }

    private static byte[] Image(int pupilX)
    {
        var pixels = new byte[640 * 400];
        for (var y = 0; y < 400; y++)
        for (var x = 0; x < 640; x++)
        {
            var pupil = (x - pupilX) * (x - pupilX) + (y - 160) * (y - 160) < 18 * 18;
            pixels[y * 640 + x] = x > 560 + y / 5 ? (byte)0 : pupil ? (byte)15 : (byte)160;
        }
        if (pupilX > 0) pixels[156 * 640 + pupilX - 3] = 255;
        return pixels;
    }
}
