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

    public const double GazeVisualScale = 4;

    public EyeSourceVisual BabbleVisual => Babble.HasSample
        ? new(
            Babble.IsOpennessValid ? Babble.Openness : Visual.Openness,
            ToVisualPupilX(Babble.GazeX),
            ToVisualPupilY(Babble.GazeY),
            Visual.PupilSize)
        : EyeSourceVisual.Hidden;

    public static double ToVisualPupilX(double gazeX) => Math.Clamp(gazeX * GazeVisualScale, -1, 1);

    public static double ToVisualPupilY(double gazeY) => Math.Clamp(-gazeY * GazeVisualScale, -1, 1);

    public EyeSourceVisual CombinedVisual => new(Combined.Openness, Combined.GazeX, Combined.GazeY, Visual.PupilSize);
}
