namespace VarjoEye.Core.Models;

public sealed record EyeCameraFrame(
    bool IsLeft,
    int Width,
    int Height,
    byte[] Pixels,
    long FrameNumber);
