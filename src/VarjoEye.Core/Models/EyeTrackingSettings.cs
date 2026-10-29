namespace VarjoEye.Core.Models;

public enum EyeTrackingFrequency
{
    MaximumSupported,
    Hz100,
    Hz200
}

public enum EyeTrackingFilter
{
    Standard,
    Raw
}

public sealed record GazeSample(
    long FrameNumber,
    EyeTrackingEye Left,
    EyeTrackingEye Right,
    long CaptureTimeNanoseconds = 0,
    int Status = 2,
    double Stability = 1,
    int LeftStatus = 3,
    int RightStatus = 3);

public sealed record EyeTrackingEye(
    double Openness,
    double PupilDiameterMm,
    double IrisDiameterMm,
    double PupilIrisRatio,
    double LegacyPupilSize,
    Vector3Value GazeOriginMeters,
    Vector3Value GazeDirection);

public sealed record VarjoHeadsetInfo(
    bool IsConnected,
    string? ProductName,
    bool? IsGazeCalibrated,
    bool? IsGazeCalibrating,
    bool? IsUserPresent,
    bool? IsGazeAllowed,
    int? LeftCalibrationQuality,
    int? RightCalibrationQuality,
    string? RuntimeVersion = null);

public sealed record VarjoHeadsetInfoResult(VarjoHeadsetInfo? Status, string? ErrorMessage = null)
{
    public bool IsSuccess => Status is not null;

    public static VarjoHeadsetInfoResult Success(VarjoHeadsetInfo status) => new(status);

    public static VarjoHeadsetInfoResult Failure(string message) => new(null, message);
}

public sealed record VarjoGazeResult(GazeSample? Sample, string? ErrorMessage = null)
{
    public bool IsSuccess => ErrorMessage is null;

    public static VarjoGazeResult Success(GazeSample? sample) => new(sample);

    public static VarjoGazeResult Failure(string message) => new(null, message);
}

public sealed record VarjoGazeBatchResult(IReadOnlyList<GazeSample> Samples, string? ErrorMessage = null)
{
    public bool IsSuccess => ErrorMessage is null;
}
