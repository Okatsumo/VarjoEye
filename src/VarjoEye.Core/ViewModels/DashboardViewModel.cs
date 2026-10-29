using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarjoEye.Core.Contracts;
using VarjoEye.Core.Models;
using VarjoEye.Core.Services.Varjo;

namespace VarjoEye.Core.ViewModels;

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan CameraUiInterval = TimeSpan.FromMilliseconds(33);

    private readonly ITrackingDataProvider _provider;
    private readonly IBaballoniaTrackingService _baballonia;
    private readonly IBaballoniaTrainer _trainer;
    private readonly IBaballoniaCalibrator _calibrator;
    private readonly ICalibrationOverlay _overlay;
    private readonly ILogService _logs;
    private readonly IUiDispatcher _dispatcher;
    private readonly object _cameraGate = new();
    private bool _isVisible;
    private EyeCameraFrame? _latestLeftCamera;
    private EyeCameraFrame? _latestRightCamera;
    private DateTimeOffset _lastCameraUi = DateTimeOffset.MinValue;
    private DateTimeOffset _lastBabbleUi = DateTimeOffset.MinValue;
    private bool _cameraUiQueued;
    private bool _babbleUiQueued;

    public DashboardViewModel(
        ITrackingDataProvider provider,
        IBaballoniaTrackingService baballonia,
        IBaballoniaTrainer trainer,
        IBaballoniaCalibrator calibrator,
        ICalibrationOverlay overlay,
        ILogService logs,
        IUiDispatcher dispatcher)
    {
        _provider = provider;
        _baballonia = baballonia;
        _trainer = trainer;
        _calibrator = calibrator;
        _overlay = overlay;
        _logs = logs;
        _dispatcher = dispatcher;
        Apply(_provider.Current);
        _provider.Updated += OnUpdated;
        _provider.CameraFrameReceived += OnCameraFrame;
        _baballonia.Updated += OnBaballoniaUpdated;
        _trainer.Changed += OnTrainerChanged;
        _calibrator.Changed += OnCalibratorChanged;
        _overlay.Changed += OnOverlayChanged;
        RefreshPrompt(_overlay.CurrentHint);
        TrainingStatusText = _trainer.StatusText;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecalibrateCommand))]
    [NotifyPropertyChangedFor(nameof(RecalibrateButtonText))]
    private HeadsetState _headset = HeadsetState.Disconnected;
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
    [ObservableProperty] private string _trainingStatusText = "Last-layer training idle";
    [ObservableProperty] private bool _isTrainingBusy;
    [ObservableProperty] private bool _isPromptVisible;
    [ObservableProperty] private string _promptTitle = "";
    [ObservableProperty] private string _promptHint = "";
    [ObservableProperty] private double _promptTargetX = 0.5;
    [ObservableProperty] private double _promptTargetY = 0.5;
    [ObservableProperty] private double _promptProgress;
    [ObservableProperty] private bool _promptShowTarget;
    [ObservableProperty] private bool _promptClosedEyes;

    public EyeMetricsDisplay LeftMetrics { get; } = new();
    public EyeMetricsDisplay RightMetrics { get; } = new();
    public string RecalibrateButtonText => Headset.IsGazeCalibrating ? "Recalibrating" : "Recalibrate";

    public void OnNavigatedTo()
    {
        _isVisible = true;
        _baballonia.SetPreviewEnabled(true);
        lock (_cameraGate)
        {
            _lastBabbleUi = DateTimeOffset.MinValue;
            _babbleUiQueued = false;
        }

        Apply(_provider.Current);
        ApplyLatestCameras(force: true);
    }

    public void OnNavigatedFrom()
    {
        _isVisible = false;
        _baballonia.SetPreviewEnabled(false);
    }

    [RelayCommand(CanExecute = nameof(CanRecalibrate))]
    private async Task RecalibrateAsync()
    {
        if (_trainer.IsBusy || _calibrator.IsCollecting)
        {
            StatusMessage = "Finish last-layer training before recalibrating.";
            IsStatusOpen = true;
            return;
        }

        var result = await Task.Run(() => _provider.RequestCalibration()).ConfigureAwait(true);
        if (result.IsSuccess)
        {
            _calibrator.BeginCollection();
            StatusMessage = _calibrator.StatusText;
            _logs.Add(LogLevel.Information, "Gaze calibration requested; Babble collection armed.");
        }
        else
        {
            StatusMessage = result.ErrorMessage ?? "Calibration request failed.";
            _logs.Add(LogLevel.Warning, StatusMessage);
        }

        IsStatusOpen = true;
    }

    private bool CanRecalibrate() =>
        Headset.Connection == ConnectionStatus.Connected && !Headset.IsGazeCalibrating;

    [RelayCommand]
    private void TrainModel()
    {
        if (_trainer.TryBegin())
        {
            IsTrainingBusy = true;
            TrainingStatusText = _trainer.StatusText;
            StatusMessage = _trainer.StatusText;
            IsStatusOpen = true;
            _logs.Add(LogLevel.Information, "Last-layer training started (120 s gaze + 20 s blink).");
            return;
        }

        StatusMessage = "Finish Varjo calibration with the headset on before training.";
        IsStatusOpen = true;
        _logs.Add(LogLevel.Warning, StatusMessage);
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
        var babble = _baballonia.Current;
        var left = Overlay(state.LeftEye, babble.LeftMetrics);
        var right = Overlay(state.RightEye, babble.RightMetrics);
        LeftCamera = CameraState.From(babble.LeftPreview, LeftCamera);
        RightCamera = CameraState.From(babble.RightPreview, RightCamera);
        LeftEye = left;
        RightEye = right;
        HasTrackingData = state.HasTrackingData;
        LeftMetrics.Update(left, state.HasTrackingData);
        RightMetrics.Update(right, state.HasTrackingData);
    }

    private void OnBaballoniaUpdated(object? sender, BaballoniaTrackingSample sample)
    {
        if (!_isVisible)
        {
            return;
        }

        lock (_cameraGate)
        {
            if (_babbleUiQueued)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (now - _lastBabbleUi < CameraUiInterval)
            {
                return;
            }

            _babbleUiQueued = true;
            _lastBabbleUi = now;
        }

        _dispatcher.Post(ApplyBabble);
    }

    private void ApplyBabble()
    {
        lock (_cameraGate)
        {
            _babbleUiQueued = false;
        }

        if (_isVisible)
        {
            Apply(_provider.Current);
        }
    }

    private static EyeSourceState Overlay(EyeSourceState eye, BabbleEyeMetrics babble) =>
        eye with { Babble = babble };

    private void OnTrainerChanged(object? sender, EventArgs e) =>
        _dispatcher.Post(() =>
        {
            TrainingStatusText = _trainer.StatusText;
            IsTrainingBusy = _trainer.IsBusy;
            if (_trainer.IsBusy || !string.IsNullOrWhiteSpace(_trainer.StatusText))
            {
                StatusMessage = _trainer.StatusText;
                IsStatusOpen = true;
            }
        });

    private void OnCalibratorChanged(object? sender, EventArgs e) =>
        _dispatcher.Post(() =>
        {
            StatusMessage = _calibrator.StatusText;
            IsStatusOpen = true;
        });

    private void OnOverlayChanged(object? sender, EventArgs e) =>
        _dispatcher.Post(() => RefreshPrompt(_overlay.CurrentHint));

    private void RefreshPrompt(CalibrationOverlayHint hint)
    {
        if (hint.Phase == CalibrationOverlayPhase.Hidden)
        {
            IsPromptVisible = false;
            return;
        }

        IsPromptVisible = true;
        PromptProgress = hint.Progress;
        PromptClosedEyes = hint.Phase == CalibrationOverlayPhase.Blink;
        PromptShowTarget = hint.Phase is CalibrationOverlayPhase.Waiting or CalibrationOverlayPhase.Sweep
            or CalibrationOverlayPhase.Blink;
        // Gaze [-1, 1], +Y up -> screen center [0, 1], +Y down. Invalid target is hidden.
        PromptShowTarget &= double.IsFinite(hint.TargetX) && double.IsFinite(hint.TargetY);
        PromptTargetX = double.IsFinite(hint.TargetX) ? Math.Clamp(hint.TargetX * 0.5 + 0.5, 0.08, 0.92) : 0.5;
        PromptTargetY = double.IsFinite(hint.TargetY) ? Math.Clamp(0.5 - hint.TargetY * 0.5, 0.08, 0.92) : 0.5;
        (PromptTitle, PromptHint) = OverlayPrompt.For(hint);
    }

    public void Dispose()
    {
        _provider.Updated -= OnUpdated;
        _provider.CameraFrameReceived -= OnCameraFrame;
        _baballonia.Updated -= OnBaballoniaUpdated;
        _trainer.Changed -= OnTrainerChanged;
        _calibrator.Changed -= OnCalibratorChanged;
        _overlay.Changed -= OnOverlayChanged;
    }
}
