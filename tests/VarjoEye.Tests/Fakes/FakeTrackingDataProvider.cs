using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;

namespace VarjoEye.Tests.Fakes;

internal sealed class FakeTrackingDataProvider : ITrackingDataProvider
{
    public EyeTrackingState Current { get; set; } = EyeTrackingState.Empty;

    public event EventHandler<EyeTrackingState>? Updated;

    public event EventHandler<EyeCameraFrame>? CameraFrameReceived;

    public event EventHandler<GazeSample>? GazeReceived;

    public long CurrentTimeNanoseconds { get; set; } = 1_000_000_000;

    public long GetCurrentTimeNanoseconds() => CurrentTimeNanoseconds;

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

    public void PublishGaze(GazeSample sample) => GazeReceived?.Invoke(this, sample);

    public void Dispose()
    {
    }
}
