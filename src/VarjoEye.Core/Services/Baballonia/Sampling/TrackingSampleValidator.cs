namespace VarjoEye.Core.Services.Baballonia.Sampling;

internal static class TrackingSampleValidator
{
    public const int Tracked = 3;
    public const int Compensated = 2;
    public const int ValidGaze = 2;
    public const double MinimumStability = 0.65;

    public static SampleRejectionReason Features(float[]? left, float[]? right, int expectedSize)
    {
        if (left is null || right is null)
            return SampleRejectionReason.MissingFeatures;
        if (left.Length != expectedSize || right.Length != expectedSize)
            return SampleRejectionReason.InvalidFeatureDimensions;
        if (!IsFinite(left) || !IsFinite(right))
            return SampleRejectionReason.NonFiniteValues;
        return SampleRejectionReason.None;
    }

    public static SampleRejectionReason Timestamp(long captureTimeNanoseconds) =>
        captureTimeNanoseconds <= 0
            ? SampleRejectionReason.InvalidTimestamp
            : SampleRejectionReason.None;

    public static SampleRejectionReason GazeFrame(int status, double stability)
    {
        if (status != ValidGaze)
            return SampleRejectionReason.InvalidTrackingStatus;
        if (!double.IsFinite(stability) || stability < MinimumStability)
            return SampleRejectionReason.LowStability;
        return SampleRejectionReason.None;
    }

    public static bool IsTrackedEye(int eyeStatus) => eyeStatus is Tracked or Compensated;

    public static bool IsFinite(float[] values)
    {
        foreach (var value in values)
        {
            if (!float.IsFinite(value))
                return false;
        }

        return true;
    }
}
