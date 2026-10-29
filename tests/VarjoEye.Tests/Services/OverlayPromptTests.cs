using VarjoEye.Core.Contracts;
using VarjoEye.Core.Services.Varjo;
using Xunit;

namespace VarjoEye.Tests.Services;

public sealed class OverlayPromptTests
{
    [Fact]
    public void Waiting_SharesDesktopAndHudTitle()
    {
        var hint = new CalibrationOverlayHint(CalibrationOverlayPhase.Waiting, 0, 0, 0, 3);
        var (title, _) = OverlayPrompt.For(hint);
        Assert.Equal("Look at the center", title);
        Assert.Equal(title, OverlayPrompt.HudLines(hint)[0]);
        Assert.Contains("Keep your head still", OverlayPrompt.HudLines(hint));
    }

    [Fact]
    public void LongSweep_IncludesHeadStillHint()
    {
        var hint = new CalibrationOverlayHint(CalibrationOverlayPhase.Sweep, 0, 0, 0, 120);
        var lines = OverlayPrompt.HudLines(hint);
        Assert.Equal("Follow the purple target", lines[0]);
        Assert.Contains("Do not move your head", lines);
    }
}
