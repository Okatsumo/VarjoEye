using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;

namespace VarjoEye.Core.ViewModels;

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan CameraUiInterval = TimeSpan.FromMilliseconds(33);

    private readonly ITrackingDataProvider _provider;
    private readonly ILogService _logs;
    private readonly IUiDispatcher _dispatcher;
    private readonly object _cameraGate = new();
    private bool _isVisible;
    private EyeCameraFrame? _latestLeftCamera;
    private EyeCameraFrame? _latestRightCamera;
    private DateTimeOffset _lastCameraUi = DateTimeOffset.MinValue;
    private bool _cameraUiQueued;

    public DashboardViewModel(
        ITrackingDataProvider provider,
        ILogService logs,
        IUiDispatcher dispatcher)
    {
        _provider = provider;
        _logs = logs;
        _dispatcher = dispatcher;
        Apply(_provider.Current);
        _provider.Updated += OnUpdated;
        _provider.CameraFrameReceived += OnCameraFrame;
    }

    [ObservableProperty] private HeadsetState _headset = HeadsetState.Disconnected;
    [ObservableProperty] private CameraState _leftCamera = CameraState.Waiting("Left Eye Camera");
    [ObservableProperty] private CameraState _rightCamera = CameraState.Waiting("Right Eye Camera");
    [ObservableProperty] private CameraState _varjoLeftCamera = CameraState.Waiting("Left IR Camera");
    [ObservableProperty] private CameraState _varjoRightCamera = CameraState.Waiting("Right IR Camera");
    [ObservableProperty] private EyeSourceState _leftEye = EyeSourceState.Empty;
    [ObservableProperty] private EyeSourceState _rightEye = EyeSourceState.Empty;
    [ObservableProperty] private bool _hasTrackingData;
    [ObservableProperty] private bool _showVarjoLayer = true;
    [ObservableProperty] private bool _showBaballoniaLayer = true;
    [ObservableProperty] private bool _showCombinedLayer = true;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isStatusOpen;

    public EyeMetricsDisplay LeftMetrics { get; } = new();
    public EyeMetricsDisplay RightMetrics { get; } = new();

    public void OnNavigatedTo()
    {
        _isVisible = true;
        Apply(_provider.Current);
        ApplyLatestCameras(force: true);
    }

    public void OnNavigatedFrom() => _isVisible = false;

    [RelayCommand]
    private void Recalibrate()
    {
        var result = _provider.RequestCalibration();
        if (result.IsSuccess)
        {
            StatusMessage = "Gaze calibration requested.";
            _logs.Add(LogLevel.Information, "Gaze calibration requested.");
        }
        else
        {
            StatusMessage = result.ErrorMessage ?? "Calibration request failed.";
            _logs.Add(LogLevel.Warning, StatusMessage);
        }

        IsStatusOpen = true;
    }

    [RelayCommand]
    private void TrainModel()
    {
        StatusMessage = "Train Model is not connected yet.";
        IsStatusOpen = true;
        _logs.Add(LogLevel.Information, "Train Model requested (not connected).");
    }

    [RelayCommand]
    private void DismissStatus() => IsStatusOpen = false;

    private void OnUpdated(object? sender, EyeTrackingState state)
    {
        if (!_isVisible)
        {
            return;
        }

        _dispatcher.Post(() => Apply(state));
        if (state.Headset.Connection != ConnectionStatus.Connected)
        {
            lock (_cameraGate)
            {
                _latestLeftCamera = null;
                _latestRightCamera = null;
            }

            _dispatcher.Post(() =>
            {
                VarjoLeftCamera = CameraState.Waiting("Left IR Camera");
                VarjoRightCamera = CameraState.Waiting("Right IR Camera");
            });
        }
    }

    private void OnCameraFrame(object? sender, EyeCameraFrame frame)
    {
        lock (_cameraGate)
        {
            if (frame.IsLeft)
            {
                _latestLeftCamera = frame;
            }
            else
            {
                _latestRightCamera = frame;
            }

            if (!_isVisible || _cameraUiQueued)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (now - _lastCameraUi < CameraUiInterval)
            {
                return;
            }

            _cameraUiQueued = true;
            _lastCameraUi = now;
        }

        _dispatcher.Post(() => ApplyLatestCameras(force: false));
    }

    private void ApplyLatestCameras(bool force)
    {
        EyeCameraFrame? left;
        EyeCameraFrame? right;
        lock (_cameraGate)
        {
            left = _latestLeftCamera;
            right = _latestRightCamera;
            _cameraUiQueued = false;
            if (force && (left is not null || right is not null))
            {
                _lastCameraUi = DateTimeOffset.UtcNow;
            }
        }

        VarjoLeftCamera = left is null
            ? CameraState.Waiting("Left IR Camera")
            : CameraState.Live("Left IR Camera", left);
        VarjoRightCamera = right is null
            ? CameraState.Waiting("Right IR Camera")
            : CameraState.Live("Right IR Camera", right);
    }

    private void Apply(EyeTrackingState state)
    {
        Headset = state.Headset;
        LeftCamera = state.LeftCamera;
        RightCamera = state.RightCamera;
        LeftEye = state.LeftEye;
        RightEye = state.RightEye;
        HasTrackingData = state.HasTrackingData;
        LeftMetrics.Update(state.LeftEye, state.HasTrackingData);
        RightMetrics.Update(state.RightEye, state.HasTrackingData);
    }

    public void Dispose()
    {
        _provider.Updated -= OnUpdated;
        _provider.CameraFrameReceived -= OnCameraFrame;
    }
}
