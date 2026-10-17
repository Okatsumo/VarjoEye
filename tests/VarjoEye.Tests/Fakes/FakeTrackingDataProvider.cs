using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;

namespace VarjoEye.Tests.Fakes;

internal sealed class FakeTrackingDataProvider : ITrackingDataProvider
{
    public EyeTrackingState Current { get; set; } = EyeTrackingState.Empty;

    public event EventHandler<EyeTrackingState>? Updated;

    public event EventHandler<EyeCameraFrame>? CameraFrameReceived;

    public OperationResult CalibrationResult { get; set; } = OperationResult.Success();

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public Task StopAsync() => Task.CompletedTask;

    public OperationResult RequestCalibration() => CalibrationResult;

    public void Publish(EyeTrackingState state)
    {
        Current = state;
        Updated?.Invoke(this, state);
    }

    public void PublishCamera(EyeCameraFrame frame) => CameraFrameReceived?.Invoke(this, frame);

    public void Dispose()
    {
    }
}
