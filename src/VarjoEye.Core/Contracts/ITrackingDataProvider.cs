using VarjoEye.Core.Models;

namespace VarjoEye.Core.Contracts;

public interface ITrackingDataProvider : IDisposable
{
    EyeTrackingState Current { get; }

    event EventHandler<EyeTrackingState>? Updated;

    event EventHandler<EyeCameraFrame>? CameraFrameReceived;

    void Start();

    void Stop();

    Task StopAsync();

    OperationResult RequestCalibration();
}
