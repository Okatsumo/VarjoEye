namespace VarjoEye.Core.Models;

public sealed record EyeTrackingState(
    HeadsetState Headset,
    CameraState LeftCamera,
    CameraState RightCamera,
    EyeSourceState LeftEye,
    EyeSourceState RightEye,
    bool HasTrackingData)
{
    public static EyeTrackingState Empty { get; } = new(
        HeadsetState.Disconnected,
        CameraState.Waiting("Left Eye Camera"),
        CameraState.Waiting("Right Eye Camera"),
        EyeSourceState.Empty,
        EyeSourceState.Empty,
        false);
}
