namespace VarjoEye.Core.Models;

public sealed record EyeTrackingState(
    HeadsetState Headset,
    CameraFrameState LeftCamera,
    CameraFrameState RightCamera,
    EyeSourceState LeftEye,
    EyeSourceState RightEye,
    bool HasTrackingData)
{
    public static EyeTrackingState Empty { get; } = new(
        HeadsetState.Disconnected,
        CameraFrameState.Waiting(EyeSide.Left),
        CameraFrameState.Waiting(EyeSide.Right),
        EyeSourceState.Empty,
        EyeSourceState.Empty,
        false);
}
