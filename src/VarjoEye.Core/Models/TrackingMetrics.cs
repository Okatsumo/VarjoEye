using System.Globalization;

namespace VarjoEye.Core.Models;

public sealed record Vector3Value(double X, double Y, double Z)
{
    public static Vector3Value Zero { get; } = new(0, 0, 0);

    public string Display =>
        string.Format(CultureInfo.InvariantCulture, "{0:0.000}, {1:0.000}, {2:0.000}", X, Y, Z);
}

public sealed record VarjoEyeMetrics(
    double Openness,
    double PupilDiameterMm,
    double IrisDiameterMm,
    double PupilIrisRatio,
    double LegacyPupilSize,
    string CalibrationQuality,
    Vector3Value GazeOriginMeters,
    Vector3Value GazeDirection)
{
    public static VarjoEyeMetrics Empty { get; } = new(
        0, 0, 0, 0, 0, "—", Vector3Value.Zero, Vector3Value.Zero);
}

public sealed record BabbleEyeMetrics(double Openness, double GazeX, double GazeY)
{
    public static BabbleEyeMetrics Empty { get; } = new(0, 0, 0);
}

public sealed record CombinedEyeMetrics(double Openness, double GazeX, double GazeY)
{
    public static CombinedEyeMetrics Empty { get; } = new(0, 0, 0);
}

public sealed record EyeSourceVisual(
    double Openness,
    double PupilX,
    double PupilY,
    double PupilSize)
{
    public static EyeSourceVisual Neutral { get; } = new(1, 0, 0, 0.55);
}
