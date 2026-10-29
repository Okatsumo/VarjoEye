namespace VarjoEye.Core.Models;

public sealed record InferenceDevice(int? GpuIndex, string Name)
{
    public static InferenceDevice Cpu { get; } = new(null, "CPU");

    public override string ToString() => Name;
}

public enum BaballoniaRuntimeStatus
{
    Disabled,
    Initializing,
    WaitingForCamera,
    Tracking,
    ModelUnavailable,
    Error,
    Stopping
}

public sealed record BaballoniaEye(double GazeX, double GazeY, double Openness);

public sealed record BaballoniaRawOutput(
    float Value0,
    float Value1,
    float Value2,
    float Value3,
    float Value4,
    float Value5)
{
    public static BaballoniaRawOutput FromArray(ReadOnlySpan<float> values) =>
        new(values[0], values[1], values[2], values[3], values[4], values[5]);

    public float[] ToFloatArray() => [Value0, Value1, Value2, Value3, Value4, Value5];
}

public sealed record CameraEyeFramePair(
    long FrameNumber,
    DateTimeOffset ReceivedAt,
    EyeCameraFrame Left,
    EyeCameraFrame Right)
{
    public long CaptureTimeNanoseconds =>
        Left.CaptureTimeNanoseconds != 0 ? Left.CaptureTimeNanoseconds : Right.CaptureTimeNanoseconds;
}

public sealed record BaballoniaInputFramePair(long FrameNumber, byte[] Left, byte[] Right, long CaptureTimeNanoseconds = 0);

public sealed record BaballoniaEyeResult(
    long FrameNumber,
    DateTimeOffset ReceivedAt,
    BaballoniaEye Left,
    BaballoniaEye Right,
    long CaptureTimeNanoseconds = 0,
    BaballoniaRawOutput? RawOutput = null,
    BaballoniaRawOutput? SmoothedOutput = null,
    float[]? LeftFeatures = null,
    float[]? RightFeatures = null);

public sealed record BaballoniaTrackingSample(
    BaballoniaRuntimeStatus Status,
    string StatusText,
    bool HasResult,
    bool IsStale,
    BaballoniaEyeResult? Result,
    CameraFrameState LeftPreview,
    CameraFrameState RightPreview)
{
    public static BaballoniaTrackingSample Empty { get; } = new(
        BaballoniaRuntimeStatus.WaitingForCamera,
        "Waiting for camera",
        false,
        false,
        null,
        CameraFrameState.Waiting(EyeSide.Left),
        CameraFrameState.Waiting(EyeSide.Right));

    public BabbleEyeMetrics LeftMetrics => Map(Result?.Left, HasResult, IsStale);

    public BabbleEyeMetrics RightMetrics => Map(Result?.Right, HasResult, IsStale);

    private static BabbleEyeMetrics Map(BaballoniaEye? eye, bool hasResult, bool stale)
    {
        if (!hasResult || eye is null)
        {
            return BabbleEyeMetrics.Empty;
        }

        var finiteGaze = double.IsFinite(eye.GazeX) && double.IsFinite(eye.GazeY);
        var finiteOpen = double.IsFinite(eye.Openness);
        return new BabbleEyeMetrics(
            true,
            finiteOpen,
            finiteGaze,
            finiteOpen ? Math.Clamp(eye.Openness, 0, 1) : 0,
            finiteGaze ? Math.Clamp(eye.GazeX, -1, 1) : 0,
            finiteGaze ? Math.Clamp(eye.GazeY, -1, 1) : 0,
            stale);
    }
}
