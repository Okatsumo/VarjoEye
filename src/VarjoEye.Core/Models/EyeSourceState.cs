namespace VarjoEye.Core.Models;

public sealed record EyeSourceState(
    EyeSourceVisual Visual,
    VarjoEyeMetrics Varjo,
    BabbleEyeMetrics Babble,
    CombinedEyeMetrics Combined)
{
    public static EyeSourceState Empty { get; } = new(
        EyeSourceVisual.Neutral,
        VarjoEyeMetrics.Empty,
        BabbleEyeMetrics.Empty,
        CombinedEyeMetrics.Empty);

    public EyeSourceVisual BabbleVisual => new(Babble.Openness, Babble.GazeX, Babble.GazeY, Visual.PupilSize);

    public EyeSourceVisual CombinedVisual => new(Combined.Openness, Combined.GazeX, Combined.GazeY, Visual.PupilSize);
}
