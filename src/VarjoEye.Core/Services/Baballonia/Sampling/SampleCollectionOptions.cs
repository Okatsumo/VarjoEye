namespace VarjoEye.Core.Services.Baballonia.Sampling;

internal sealed record SampleCollectionOptions(
    int GazeCapacity,
    long MaxPairDeltaNanoseconds,
    long MinSampleSpacingNanoseconds = 0)
{
    public static SampleCollectionOptions Trainer { get; } = new(256, 12_000_000, 20_000_000);

    public static SampleCollectionOptions Calibrator { get; } = new(128, 12_000_000);
}
