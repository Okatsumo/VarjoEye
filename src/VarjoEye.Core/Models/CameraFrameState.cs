namespace VarjoEye.Core.Models;

public enum EyeSide { Left, Right }
public enum CameraAvailability { Waiting, Streaming, Unavailable, Error }
public enum CameraPixelFormat { Gray8 }

/// <summary>Published buffers are managed, immutable after publication and retained by this snapshot.</summary>
public readonly record struct CameraFrameState(
    EyeSide Eye,
    CameraAvailability Availability,
    int Width = 0,
    int Height = 0,
    byte[]? Pixels = null,
    long TimestampNanoseconds = 0,
    CameraPixelFormat PixelFormat = CameraPixelFormat.Gray8,
    string? Error = null)
{
    public static CameraFrameState Waiting(EyeSide eye) => new(eye, CameraAvailability.Waiting);
}
