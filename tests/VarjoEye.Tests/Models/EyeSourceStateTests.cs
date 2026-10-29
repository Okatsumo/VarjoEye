using VarjoEye.Core.Models;
using Xunit;

namespace VarjoEye.Tests.Models;

public sealed class EyeSourceStateTests
{
    [Fact]
    public void BabbleVisual_ScalesGazeIntoVarjoPupilSpace()
    {
        var eye = EyeSourceState.Empty with
        {
            Visual = new EyeSourceVisual(1, 0, 0, 0.55),
            Babble = new BabbleEyeMetrics(true, true, true, 0.9, 0.2, -0.1)
        };

        Assert.Equal(0.8, eye.BabbleVisual.PupilX);
        Assert.Equal(0.4, eye.BabbleVisual.PupilY);
        Assert.Equal(0.9, eye.BabbleVisual.Openness);
        Assert.Equal(0.2, eye.Babble.GazeX);
        Assert.Equal(-0.1, eye.Babble.GazeY);
    }
}
