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

    OperationResult RequestGazeCalibration();

    OperationResult StartEyeCameraStream(EyeTrackingFrequency frequency);

    void StopEyeCameraStream();

    void Disconnect();
}
