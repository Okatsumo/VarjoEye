namespace VarjoEye.Core.Models;

public sealed record CameraState(
    string Label,
    bool IsAvailable,
    string? ImageAssetKey,
    int Width = 0,
    int Height = 0,
    byte[]? Pixels = null)
{
    public static CameraState Waiting(string label) => new(label, false, null);

    public static CameraState Live(string label, EyeCameraFrame frame) =>
        new(label, true, null, frame.Width, frame.Height, frame.Pixels);
}
