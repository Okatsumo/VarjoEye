using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;

namespace VarjoEye.Tests.Fakes;

internal sealed class FakeVarjoRuntime : IVarjoRuntime
{
    private readonly object _gate = new();
    private readonly Queue<OperationResult> _connectResults = new();
    private int _inFlight;
    private bool _sessionOpen;
    private bool _eyeTrackingInitialized;
    private bool _cameraStreaming;

    public int ConnectCount { get; private set; }

    public int DisconnectCount { get; private set; }

    public int InitializeCount { get; private set; }

    public int StartCameraCount { get; private set; }

    public int StopCameraCount { get; private set; }

    public EyeTrackingFrequency? LastCameraFrequency { get; private set; }

    public OperationResult CameraStreamResult { get; set; } = OperationResult.Success();

    public bool DisconnectedWhileBusy { get; private set; }

    public bool HeadsetConnected { get; set; } = true;

    public bool IsSessionOpen
    {
        get
        {
            lock (_gate)
            {
                return _sessionOpen;
            }
        }
    }

    public bool IsEyeTrackingInitialized
    {
        get
        {
            lock (_gate)
            {
                return _eyeTrackingInitialized;
            }
        }
    }

    public bool IsEyeCameraStreaming
    {
        get
        {
            lock (_gate)
            {
                return _cameraStreaming;
            }
        }
    }

    public event EventHandler<EyeCameraFrame>? EyeCameraFrameReceived
    {
        add { }
        remove { }
    }

    public void EnqueueConnectResult(OperationResult result)
    {
        lock (_gate)
        {
            _connectResults.Enqueue(result);
        }
    }

    public OperationResult Connect()
    {
        using var _ = EnterNative();
        lock (_gate)
        {
            ConnectCount++;
            if (_sessionOpen)
            {
                return OperationResult.Success();
            }

            if (_connectResults.Count > 0)
            {
                var queued = _connectResults.Dequeue();
                if (queued.IsSuccess)
                {
                    _sessionOpen = true;
                }

                return queued;
            }

            _sessionOpen = true;
            return OperationResult.Success();
        }
    }

    public VarjoHeadsetInfoResult GetHeadsetStatus()
    {
        using var _ = EnterNative();
        lock (_gate)
        {
            if (!_sessionOpen)
            {
                return VarjoHeadsetInfoResult.Failure("Open a Varjo session first.");
            }

            if (!HeadsetConnected)
            {
                _eyeTrackingInitialized = false;
            }

            return VarjoHeadsetInfoResult.Success(new VarjoHeadsetInfo(
                HeadsetConnected,
                HeadsetConnected ? "Varjo XR-4" : null,
                true,
                false,
                true,
                true,
                3,
                3));
        }
    }

    public OperationResult InitializeEyeTracking(EyeTrackingFrequency frequency, EyeTrackingFilter filter)
    {
        using var _ = EnterNative();
        lock (_gate)
        {
            if (!_sessionOpen)
            {
                return OperationResult.Failure("Open a Varjo session first.");
            }

            InitializeCount++;
            _eyeTrackingInitialized = true;
            return OperationResult.Success();
        }
    }

    public VarjoGazeResult GetGazeSample()
    {
        using var _ = EnterNative();
        lock (_gate)
        {
            if (!_sessionOpen || !_eyeTrackingInitialized)
            {
                return VarjoGazeResult.Failure("Eye tracking is not initialized.");
            }

            var eye = new EyeTrackingEye(1, 4.1, 12, 0.3, 0.5, new Vector3Value(0, 0, 0), new Vector3Value(0, 0, 1));
            return VarjoGazeResult.Success(new GazeSample(1, eye, eye));
        }
    }

    public OperationResult RequestGazeCalibration() => OperationResult.Success();

    public OperationResult StartEyeCameraStream(EyeTrackingFrequency frequency)
    {
        using var _ = EnterNative();
        lock (_gate)
        {
            StartCameraCount++;
            LastCameraFrequency = frequency;
            if (!CameraStreamResult.IsSuccess)
            {
                _cameraStreaming = false;
                return CameraStreamResult;
            }

            _cameraStreaming = true;
            return OperationResult.Success();
        }
    }

    public void StopEyeCameraStream()
    {
        lock (_gate)
        {
            StopCameraCount++;
            _cameraStreaming = false;
        }
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _inFlight) > 0)
            {
                DisconnectedWhileBusy = true;
            }

            DisconnectCount++;
            _sessionOpen = false;
            _eyeTrackingInitialized = false;
            if (_cameraStreaming)
            {
                StopCameraCount++;
                _cameraStreaming = false;
            }
        }
    }

    public void Dispose() => Disconnect();

    private NativeScope EnterNative()
    {
        Interlocked.Increment(ref _inFlight);
        return new NativeScope(this);
    }

    private readonly struct NativeScope(FakeVarjoRuntime owner) : IDisposable
    {
        public void Dispose() => Interlocked.Decrement(ref owner._inFlight);
    }
}
