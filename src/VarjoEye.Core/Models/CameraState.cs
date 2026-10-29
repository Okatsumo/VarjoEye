namespace VarjoEye.Core.Models;

public sealed record CameraState(
    string Label,
    bool IsAvailable,
    int Width = 0,
    int Height = 0,
    byte[]? Pixels = null)
{
    public static CameraState From(CameraFrameState state, CameraState? previous = null)
    {
        var available = state.Availability == CameraAvailability.Streaming && state.Pixels is not null;
        var label = state.Eye == EyeSide.Left ? "Left Eye Camera" : "Right Eye Camera";
        if (previous is not null && previous.Label == label && previous.IsAvailable == available &&
            previous.Width == state.Width && previous.Height == state.Height && ReferenceEquals(previous.Pixels, state.Pixels))
            return previous;
        return new(label, available, state.Width, state.Height, state.Pixels);
    }

    public static CameraState Waiting(string label) => new(label, false);

    public static CameraState Live(string label, EyeCameraFrame frame) =>
        new(label, true, frame.Width, frame.Height, frame.Pixels);
}
