using VarjoEye.Core.Models;

namespace VarjoEye.Core.Contracts;

public interface IVarjoRuntime : IDisposable
{
    bool IsSessionOpen { get; }

    bool IsEyeTrackingInitialized { get; }

    bool IsEyeCameraStreaming { get; }

    event EventHandler<EyeCameraFrame>? EyeCameraFrameReceived;

    OperationResult Connect();

    VarjoHeadsetInfoResult GetHeadsetStatus();

    OperationResult InitializeEyeTracking(EyeTrackingFrequency frequency, EyeTrackingFilter filter);

    VarjoGazeResult GetGazeSample();

    // Default preserves compatibility with simple runtimes; native runtime drains the SDK queue.
    VarjoGazeBatchResult GetGazeSamples()
    {
        var result = GetGazeSample();
        return new(result.Sample is { } sample ? [sample] : [], result.ErrorMessage);
    }

    long GetCurrentTimeNanoseconds();

    OperationResult RequestGazeCalibration();

    OperationResult StartEyeCameraStream(EyeTrackingFrequency frequency);

    void StopEyeCameraStream();

    void Disconnect();
}
