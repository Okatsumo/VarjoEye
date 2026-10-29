using VarjoEye.Core.Models;

namespace VarjoEye.Core.Contracts;

public interface ITrackingDataProvider : IDisposable
{
    EyeTrackingState Current { get; }

    /// <summary>
    /// Sequential state snapshots on a dedicated worker. Pending states may be coalesced
    /// to the latest when a consumer is slow. Current advances independently;
    /// an active callback can finish after StopAsync returns.
    /// </summary>
    event EventHandler<EyeTrackingState>? Updated;

    event EventHandler<EyeCameraFrame>? CameraFrameReceived;

    event EventHandler<GazeSample>? GazeReceived;

    long GetCurrentTimeNanoseconds();

    void Start();

    void Stop();

    Task StopAsync();

    OperationResult RequestCalibration();
}
