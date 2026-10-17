using VarjoEye.Core.Models;
using VarjoEye.Core.Services.Varjo;
using Xunit;

namespace VarjoEye.Tests.Services;

public sealed class EyeCameraStreamSelectorTests
{
    [Fact]
    public void SelectsExactRateThenLargestResolution()
    {
        EyeCameraStreamOption[] options =
        [
            new(1, 3, 100, 640, 480),
            new(2, 3, 200, 400, 400),
            new(3, 3, 200, 800, 800),
            new(4, 3, 50, 1200, 1200)
        ];

        var selected = EyeCameraStreamSelector.Select(options, EyeTrackingFrequency.Hz200);

        Assert.NotNull(selected);
        Assert.Equal(3, selected.Value.StreamId);
        Assert.Equal(200, selected.Value.FrameRate);
        Assert.Equal(800, selected.Value.Width);
    }

    [Fact]
    public void MaximumSupported_PicksHighestRateThenLargestFrame()
    {
        EyeCameraStreamOption[] options =
        [
            new(1, 3, 100, 1280, 800),
            new(2, 3, 200, 400, 400),
            new(3, 3, 200, 640, 480)
        ];

        var selected = EyeCameraStreamSelector.Select(options, EyeTrackingFrequency.MaximumSupported);

        Assert.NotNull(selected);
        Assert.Equal(3, selected.Value.StreamId);
    }

    [Fact]
    public void MissingExactRate_UsesClosestBelow()
    {
        EyeCameraStreamOption[] options =
        [
            new(1, 3, 90, 800, 800),
            new(2, 3, 50, 1280, 800)
        ];

        var selected = EyeCameraStreamSelector.Select(options, EyeTrackingFrequency.Hz100);

        Assert.NotNull(selected);
        Assert.Equal(1, selected.Value.StreamId);
    }
}
